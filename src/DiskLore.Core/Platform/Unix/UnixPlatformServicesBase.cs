using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskLore.Core.Programs;

namespace DiskLore.Core.Platform;

/// <summary>Shared behaviour of the Linux and macOS services.</summary>
public abstract partial class UnixPlatformServicesBase : IPlatformServices
{
    private sealed class EmptyPrograms : IInstalledProgramProvider
    {
        public IReadOnlyList<InstalledProgram> GetInstalled() => Array.Empty<InstalledProgram>();
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();

    protected UnixPlatformServicesBase(string appDataDirectory)
    {
        AppDataDirectory = appDataDirectory;
        try { Directory.CreateDirectory(AppDataDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public string AppDataDirectory { get; }
    public IInstalledProgramProvider Programs { get; } = new EmptyPrograms();

    protected static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Whether a mounted file system should be listed as a drive.</summary>
    protected abstract bool IncludeDrive(string rootPath, string format);

    protected virtual string FallbackLabel(string rootPath) => Path.GetFileName(rootPath.TrimEnd('/')) is { Length: > 0 } n ? n : "Disk";

    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var root = d.RootDirectory.FullName;
                var format = d.DriveFormat;
                if (!IncludeDrive(root, format)) continue;
                if (d.TotalSize <= 0) continue;
                var label = string.Empty;
                try { label = d.VolumeLabel; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (string.IsNullOrWhiteSpace(label) || label == root) label = FallbackLabel(root);
                list.Add(new DriveEntry(root, label, format, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    public bool SupportsLockDetection => false;
    public IReadOnlyList<LockingProcess> FindLockingProcesses(IReadOnlyList<string> filePaths) => Array.Empty<LockingProcess>();

    public abstract DeleteResult MoveToTrash(string path);

    public DeleteResult DeletePermanently(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists && !Directory.Exists(path) && info.LinkTarget == null)
                return new DeleteResult(true, null);

            bool isLink = info.LinkTarget != null;
            if (Directory.Exists(path) && !isLink)
                return DeleteTree(path);

            // Files and symbolic links (to files or directories): remove the entry itself.
            if (Directory.Exists(path)) Directory.Delete(path, false);
            else File.Delete(path);
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

        var dirs = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            dirs.Add(dir);
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail(ex, dir); continue; }

            foreach (var entry in entries)
            {
                try
                {
                    bool isLink = entry.LinkTarget != null || (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                    if (isDir && !isLink) { stack.Push(entry.FullName); continue; }
                    if (isDir) Directory.Delete(entry.FullName, false); // symlink to a directory
                    else File.Delete(entry.FullName);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail(ex, entry.FullName); }
            }
        }

        for (int i = dirs.Count - 1; i >= 0; i--)
        {
            try { Directory.Delete(dirs[i], false); }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail(ex, dirs[i]); }
        }

        return failures == 0
            ? new DeleteResult(true, null)
            : new DeleteResult(false, $"{failures} item(s) could not be deleted. First error: {firstError}");
    }

    public abstract void RevealInFileManager(string path);
    public abstract void OpenUri(string uri);

    public void RunCommandElevated(string commandLine) { }
    public bool SupportsElevatedCommands => false;

    public bool IsElevated
    {
        get
        {
            try { return GetEuid() == 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return Environment.UserName == "root";
            }
        }
    }

    public void RestartElevated(string? scanPath) { }

    public bool SupportsShellIntegration => false;
    public bool IsShellIntegrationEnabled() => false;
    public void SetShellIntegration(bool enabled, string exePath) { }

    /// <summary>Runs a tool and waits for it; returns exit code and stderr, or null when it could not start.</summary>
    protected static (int ExitCode, string Error)? RunTool(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var err = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, err.Result.Trim());
        }
        catch (Win32Exception) { return null; }
    }

    /// <summary>Starts a viewer process without waiting for it.</summary>
    protected static void StartDetached(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try { Process.Start(psi)?.Dispose(); }
        catch (Win32Exception) { }
    }

    protected static string UniqueName(string name, Func<string, bool> exists)
    {
        if (!exists(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            var candidate = $"{stem}.{i}{ext}";
            if (!exists(candidate)) return candidate;
        }
    }
}
