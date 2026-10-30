using System.Runtime.Versioning;
using DiskLore.Core.Programs;
using Microsoft.Win32;

namespace DiskLore.Core.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsProgramProvider : IInstalledProgramProvider
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string StorePackagesKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private readonly object _gate = new();
    private IReadOnlyList<InstalledProgram>? _cache;

    public IReadOnlyList<InstalledProgram> GetInstalled()
    {
        lock (_gate)
        {
            return _cache ??= Load();
        }
    }

    private static IReadOnlyList<InstalledProgram> Load()
    {
        var list = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, list, seen);
        ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, list, seen);
        ReadUninstall(RegistryHive.CurrentUser, RegistryView.Default, list, seen);
        ReadStorePackages(list);
        return list;
    }

    private static void ReadUninstall(RegistryHive hive, RegistryView view, List<InstalledProgram> list, HashSet<string> seen)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallKey);
            if (root == null) return;

            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key == null) continue;
                    var program = ReadProgram(key);
                    if (program == null) continue;

                    // The 64-bit and 32-bit views (and HKCU) can list the same program twice.
                    var dedupe = program.DisplayName + "|" + program.Version + "|" + program.InstallLocation;
                    if (!seen.Add(dedupe)) continue;
                    list.Add(program);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    private static InstalledProgram? ReadProgram(RegistryKey key)
    {
        var displayName = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(displayName)) return null;
        if (key.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (!string.IsNullOrEmpty(key.GetValue("ParentKeyName") as string)) return null;

        var uninstall = (key.GetValue("UninstallString") as string)?.Trim();
        var location = CleanLocation(key.GetValue("InstallLocation") as string);

        if (location == null)
        {
            location = DeriveFromExe(key.GetValue("DisplayIcon") as string)
                       ?? DeriveFromExe(uninstall);
        }

        return new InstalledProgram(
            displayName,
            NullIfEmpty(key.GetValue("Publisher") as string),
            location,
            NullIfEmpty(key.GetValue("DisplayVersion") as string),
            NullIfEmpty(uninstall),
            false,
            null);
    }

    private static void ReadStorePackages(List<InstalledProgram> list)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(StorePackagesKey);
            if (root == null) return;

            foreach (var fullName in root.GetSubKeyNames())
            {
                // <name>_<version>_<arch>_<resourceId>_<publisherId>
                var parts = fullName.Split('_');
                if (parts.Length < 5) continue;
                if (parts[3].Length > 0) continue; // language / scale resource packages

                string? path = null;
                string? display = null;
                try
                {
                    using var pkg = root.OpenSubKey(fullName);
                    path = CleanLocation(pkg?.GetValue("PackageRootFolder") as string)
                           ?? CleanLocation(pkg?.GetValue("Path") as string);
                    display = (pkg?.GetValue("DisplayName") as string)?.Trim();
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }

                if (string.IsNullOrEmpty(display) || display.StartsWith('@') || display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    display = parts[0];

                list.Add(new InstalledProgram(
                    display,
                    null,
                    path,
                    parts[1],
                    null,
                    true,
                    parts[0] + "_" + parts[4]));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    private static string? NullIfEmpty(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    internal static string? CleanLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Trim('"').Trim();
        s = Environment.ExpandEnvironmentVariables(s);
        if (s.Length > 3) s = s.TrimEnd('\\', '/');
        else if (s.Length == 2 && s[1] == ':') s += "\\";
        if (s.Length == 0) return null;
        // Drive root alone would match everything.
        if (s.Length <= 3 && s.EndsWith('\\')) return null;
        return s;
    }

    /// <summary>
    /// Extracts the directory of the executable named in a DisplayIcon / UninstallString value,
    /// unless that directory is a generic location shared by many programs.
    /// </summary>
    internal static string? DeriveFromExe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        string exe;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end < 0) return null;
            exe = s.Substring(1, end - 1);
        }
        else
        {
            // Unquoted: cut at the first ".exe" (case-insensitive), dropping ",0" icon indexes and arguments.
            var idx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) exe = s.Substring(0, idx + 4);
            else
            {
                var comma = s.IndexOf(',');
                exe = comma > 0 ? s.Substring(0, comma) : s;
            }
        }

        exe = Environment.ExpandEnvironmentVariables(exe.Trim());
        if (exe.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return null;
        string? dir;
        try
        {
            if (!Path.IsPathRooted(exe)) return null;
            dir = Path.GetDirectoryName(exe);
        }
        catch (ArgumentException) { return null; }

        dir = CleanLocation(dir);
        if (dir == null || IsGenericDirectory(dir)) return null;
        return dir;
    }

    internal static bool IsGenericDirectory(string dir)
    {
        dir = dir.TrimEnd('\\', '/');
        var name = Path.GetFileName(dir);
        if (name.Length == 0) return true; // drive root

        string[] genericNames =
        {
            "Windows", "System32", "SysWOW64", "Installer", "Package Cache", "Program Files", "Program Files (x86)",
            "ProgramData", "Users", "Common Files", "WindowsApps", "Temp", "Downloads",
        };
        foreach (var g in genericNames)
            if (name.Equals(g, StringComparison.OrdinalIgnoreCase)) return true;

        string[] genericRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var r in genericRoots)
        {
            if (string.IsNullOrEmpty(r)) continue;
            if (string.Equals(r.TrimEnd('\\', '/'), dir, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Installer caches hold per-product subfolders that are not the program's own folder.
        foreach (var seg in dir.Split(new[] { '\\', '/' }))
            if (seg.Equals("Package Cache", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals("Installer", StringComparison.OrdinalIgnoreCase))
                return true;

        // Anything inside the Windows directory belongs to the OS.
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');
        if (win.Length > 0 && dir.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
