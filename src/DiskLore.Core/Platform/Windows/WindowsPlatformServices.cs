using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using DiskLore.Core.Programs;
using Microsoft.Win32;

namespace DiskLore.Core.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
    private const int ErrorCancelled = 1223;
    private const string ShellKeyName = "DiskLore";
    private static readonly string[] ShellParents =
    {
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Directory\Background\shell",
        @"Software\Classes\Drive\shell",
    };

    public WindowsPlatformServices()
    {
        AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskLore");
        try { Directory.CreateDirectory(AppDataDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Programs = new WindowsProgramProvider();
    }

    public string AppDataDirectory { get; }

    public IInstalledProgramProvider Programs { get; }

    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType == DriveType.CDRom && d.TotalSize <= 0) continue;
                var label = string.Empty;
                try { label = d.VolumeLabel; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (string.IsNullOrWhiteSpace(label)) label = "Local Disk";
                list.Add(new DriveEntry(d.RootDirectory.FullName, label, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    // ---- Lock detection (Restart Manager) ----

    public bool SupportsLockDetection => true;

    public unsafe IReadOnlyList<LockingProcess> FindLockingProcesses(IReadOnlyList<string> filePaths)
    {
        var result = new List<LockingProcess>();
        if (filePaths.Count == 0) return result;

        char* key = stackalloc char[33];
        if (WindowsInterop.RmStartSession(out uint session, 0, key) != WindowsInterop.ERROR_SUCCESS)
            return result;

        try
        {
            const int chunk = 500;
            int registered = 0;
            for (int i = 0; i < filePaths.Count; i += chunk)
            {
                var batch = new List<string>(chunk);
                for (int j = i; j < Math.Min(i + chunk, filePaths.Count); j++)
                {
                    var p = filePaths[j];
                    if (File.Exists(p)) batch.Add(p); // vanished files and directories are skipped
                }
                if (batch.Count == 0) continue;

                var ptrs = new nint[batch.Count];
                try
                {
                    for (int k = 0; k < batch.Count; k++) ptrs[k] = Marshal.StringToHGlobalUni(batch[k]);
                    fixed (nint* pp = ptrs)
                    {
                        if (WindowsInterop.RmRegisterResources(session, (uint)batch.Count, pp, 0, 0, 0, 0) == WindowsInterop.ERROR_SUCCESS)
                            registered += batch.Count;
                    }
                }
                finally
                {
                    foreach (var p in ptrs) if (p != 0) Marshal.FreeHGlobal(p);
                }
            }
            if (registered == 0) return result;

            uint have = 0;
            uint needed;
            uint reasons;
            int rc = WindowsInterop.RmGetList(session, out needed, ref have, null, out reasons);
            for (int attempt = 0; rc == WindowsInterop.ERROR_MORE_DATA && attempt < 5; attempt++)
            {
                var buffer = new WindowsInterop.RM_PROCESS_INFO[needed + 4];
                have = (uint)buffer.Length;
                fixed (WindowsInterop.RM_PROCESS_INFO* pi = buffer)
                {
                    rc = WindowsInterop.RmGetList(session, out needed, ref have, pi, out reasons);
                    if (rc == WindowsInterop.ERROR_SUCCESS)
                    {
                        for (int i = 0; i < have; i++)
                        {
                            int pid = (int)pi[i].Process.dwProcessId;
                            string? appName = new string(pi[i].strAppName);
                            if (string.IsNullOrWhiteSpace(appName)) appName = null;
                            result.Add(new LockingProcess(pid, ProcessName(pid, appName), appName));
                        }
                    }
                }
            }
        }
        finally
        {
            WindowsInterop.RmEndSession(session);
        }
        return result;
    }

    private static string ProcessName(int pid, string? fallback)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return fallback ?? ("pid " + pid);
        }
    }

    // ---- Delete ----

    public unsafe DeleteResult MoveToTrash(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return new DeleteResult(true, null);

        // pFrom must be double-null-terminated.
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + "\0\0");
        try
        {
            var op = new WindowsInterop.SHFILEOPSTRUCTW
            {
                wFunc = WindowsInterop.FO_DELETE,
                pFrom = from,
                fFlags = (ushort)(WindowsInterop.FOF_ALLOWUNDO | WindowsInterop.FOF_NOCONFIRMATION |
                                  WindowsInterop.FOF_WANTNUKEWARNING | WindowsInterop.FOF_SILENT),
            };
            int rc = WindowsInterop.SHFileOperation(ref op);
            if (rc != 0)
                return new DeleteResult(false, $"The Recycle Bin operation failed (code 0x{rc:X}).");
            if (op.fAnyOperationsAborted != 0)
                return new DeleteResult(false, "The operation was cancelled.");
            return new DeleteResult(true, null);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    public DeleteResult DeletePermanently(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            bool isDir = (attrs & FileAttributes.Directory) != 0;
            bool isLink = (attrs & FileAttributes.ReparsePoint) != 0;

            if (!isDir)
            {
                ClearReadOnly(path, attrs);
                File.Delete(path);
                return new DeleteResult(true, null);
            }

            if (isLink)
            {
                // Remove the link itself, never what it points to.
                ClearReadOnly(path, attrs);
                Directory.Delete(path, false);
                return new DeleteResult(true, null);
            }

            return DeleteTree(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DeleteResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DeleteResult(false, ex.Message);
        }
    }

    private static DeleteResult DeleteTree(string root)
    {
        int failures = 0;
        string? firstError = null;
        void Fail(Exception ex, string p)
        {
            failures++;
            firstError ??= $"{p}: {ex.Message}";
        }

        // Pass 1: walk down without ever entering a link; delete files and links as they are met.
        var dirs = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            dirs.Add(dir);
            IEnumerable<FileSystemInfo> entries;
            List<FileSystemInfo> snapshot;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos();
                snapshot = entries.ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(ex, dir);
                continue;
            }

            foreach (var entry in snapshot)
            {
                try
                {
                    bool isLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                    if (isDir && !isLink)
                    {
                        stack.Push(entry.FullName);
                        continue;
                    }
                    ClearReadOnly(entry.FullName, entry.Attributes);
                    if (isDir) Directory.Delete(entry.FullName, false); // directory junction / symlink: link only
                    else File.Delete(entry.FullName);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Fail(ex, entry.FullName);
                }
            }
        }

        // Pass 2: remove the now-empty directories, deepest first.
        for (int i = dirs.Count - 1; i >= 0; i--)
        {
            var dir = dirs[i];
            try
            {
                ClearReadOnly(dir, File.GetAttributes(dir));
                Directory.Delete(dir, false);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(ex, dir);
            }
        }

        if (failures == 0) return new DeleteResult(true, null);
        return new DeleteResult(false, $"{failures} item(s) could not be deleted. First error: {firstError}");
    }

    private static void ClearReadOnly(string path, FileAttributes attrs)
    {
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
    }

    // ---- Shell ----

    public void RevealInFileManager(string path)
    {
        var psi = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            Arguments = $"/select,\"{path}\"",
        };
        Process.Start(psi)?.Dispose();
    }

    public void OpenUri(string uri)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
    }

    public bool SupportsElevatedCommands => true;

    public void RunCommandElevated(string commandLine)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NoExit");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(commandLine);
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user declined the UAC prompt.
        }
    }

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Starts an elevated copy of this program. Throws <see cref="OperationCanceledException"/> when the user
    /// declines the UAC prompt; the caller must then keep running and only shut down when no exception was thrown.
    /// </summary>
    public void RestartElevated(string? scanPath)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
        };
        if (!string.IsNullOrEmpty(scanPath)) psi.Arguments = "\"" + scanPath + (scanPath.EndsWith('\\') ? "\\" : "") + "\"";
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("The user declined the elevation prompt.", ex);
        }
    }

    // ---- Explorer context menu (HKCU only) ----

    public bool SupportsShellIntegration => true;

    public bool IsShellIntegrationEnabled()
    {
        try
        {
            using var cmd = Registry.CurrentUser.OpenSubKey(ShellParents[0] + "\\" + ShellKeyName + "\\command");
            var value = cmd?.GetValue(null) as string;
            if (string.IsNullOrEmpty(value)) return false;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            return value.Contains(exe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public void SetShellIntegration(bool enabled, string exePath)
    {
        foreach (var parent in ShellParents)
        {
            var path = parent + "\\" + ShellKeyName;
            if (!enabled)
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, false);
                continue;
            }

            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue(null, "Open in DiskLore");
            key.SetValue("MUIVerb", "Open in DiskLore");
            key.SetValue("Icon", exePath);
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue(null, "\"" + exePath + "\" \"%V\"");
        }
    }
}
