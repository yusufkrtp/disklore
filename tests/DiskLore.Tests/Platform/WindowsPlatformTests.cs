using System.Diagnostics;
using DiskLore.Core.Platform;
using Microsoft.Win32;
using Xunit;

namespace DiskLore.Tests.Platform;

public class PlatformCommonTests
{
    [Fact]
    public void Create_Returns_Services_For_This_OS_With_Existing_AppData()
    {
        var p = PlatformServices.Create();
        Assert.True(Directory.Exists(p.AppDataDirectory));
        Assert.EndsWith("DiskLore", p.AppDataDirectory);
        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsPlatformServices>(p);
        Assert.NotEmpty(p.GetDrives());
    }

    [Fact]
    public void DeletePermanently_Removes_Tree_With_ReadOnly_File()
    {
        var p = PlatformServices.Create();
        var root = Path.Combine(Path.GetTempPath(), "dl_del_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        var f = Path.Combine(root, "a", "b", "ro.txt");
        File.WriteAllText(f, "x");
        File.SetAttributes(f, FileAttributes.ReadOnly);
        File.WriteAllText(Path.Combine(root, "top.txt"), "y");

        var r = p.DeletePermanently(root);
        Assert.True(r.Success, r.Error);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void DeletePermanently_Of_Missing_Path_Succeeds()
    {
        var p = PlatformServices.Create();
        Assert.True(p.DeletePermanently(Path.Combine(Path.GetTempPath(), "dl_missing_" + Guid.NewGuid().ToString("N"))).Success);
    }
}

public class WindowsPlatformTests
{
    private static WindowsPlatformServices? Svc() =>
        OperatingSystem.IsWindows() ? new WindowsPlatformServices() : null;

    [Fact]
    public void Registry_Provider_Lists_Programs_With_Install_Locations()
    {
        var s = Svc();
        if (s == null) return;
        var list = s.Programs.GetInstalled();
        Assert.NotEmpty(list);
        Assert.Contains(list, p => !string.IsNullOrEmpty(p.InstallLocation) && !p.IsStoreApp);
        Assert.All(list, p => Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
        Assert.Same(list, s.Programs.GetInstalled());   // cached
    }

    [Fact]
    public void Lock_Detection_Finds_This_Process()
    {
        var s = Svc();
        if (s == null) return;
        var file = Path.Combine(Path.GetTempPath(), "dl_lock_" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var fs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            try
            {
                var locking = s.FindLockingProcesses(new[] { file, file + ".vanished" });
                Assert.Contains(locking, l => l.Pid == Environment.ProcessId);
            }
            finally
            {
                fs.Dispose();
                File.Delete(file);
            }
        }
        Assert.Empty(s.FindLockingProcesses(new[] { file }));   // vanished file: no crash, nothing reported
    }

    [Fact]
    public void DeletePermanently_Does_Not_Follow_Junctions()
    {
        var s = Svc();
        if (s == null) return;
        var baseDir = Path.Combine(Path.GetTempPath(), "dl_junc_" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(baseDir, "target");
        var tree = Path.Combine(baseDir, "tree");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(tree, "inner"));
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        File.WriteAllText(Path.Combine(target, "sub", "keep2.txt"), "keep");
        File.WriteAllText(Path.Combine(tree, "inner", "gone.txt"), "gone");

        var junction = Path.Combine(tree, "inner", "link");
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var proc = Process.Start(psi)!) { proc.WaitForExit(); Assert.Equal(0, proc.ExitCode); }
            Assert.True(Directory.Exists(junction));

            var r = s.DeletePermanently(tree);
            Assert.True(r.Success, r.Error);
            Assert.False(Directory.Exists(tree));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(target, "sub", "keep2.txt")));
        }
        finally
        {
            try { if (Directory.Exists(junction)) Directory.Delete(junction, false); } catch (IOException) { }
            try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void MoveToTrash_Removes_A_Tiny_File()
    {
        var s = Svc();
        if (s == null) return;
        var file = Path.Combine(Path.GetTempPath(), "dl_trash_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "t");
        var r = s.MoveToTrash(file);
        Assert.True(r.Success, r.Error);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Shell_Integration_Round_Trip()
    {
        var s = Svc();
        if (s == null) return;
        var fake = @"C:\DiskLoreTest\fake-" + Guid.NewGuid().ToString("N") + ".exe";
        string[] keys =
        {
            @"Software\Classes\Directory\shell\DiskLore",
            @"Software\Classes\Directory\Background\shell\DiskLore",
            @"Software\Classes\Drive\shell\DiskLore",
        };

        // Do not clobber a real installation of the user's.
        foreach (var k in keys)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(k);
            if (existing != null) return;
        }

        try
        {
            s.SetShellIntegration(true, fake);
            foreach (var k in keys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(k);
                Assert.NotNull(key);
                Assert.Equal("Open in DiskLore", key!.GetValue(null));
                Assert.Equal(fake, key.GetValue("Icon"));
                using var cmd = key.OpenSubKey("command");
                Assert.Equal($"\"{fake}\" \"%V\"", cmd!.GetValue(null));
            }
            Assert.False(s.IsShellIntegrationEnabled());   // points at a different exe than this process
        }
        finally
        {
            s.SetShellIntegration(false, fake);
        }

        foreach (var k in keys)
        {
            using var key = Registry.CurrentUser.OpenSubKey(k);
            Assert.Null(key);
        }
    }

    [Fact]
    public void Derive_From_Exe_Skips_Generic_Directories()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(WindowsProgramProvider.DeriveFromExe(@"C:\Windows\System32\msiexec.exe /x {GUID}"));
        Assert.Null(WindowsProgramProvider.DeriveFromExe(@"""C:\Windows\Installer\{GUID}\icon.exe"""));
        Assert.Null(WindowsProgramProvider.DeriveFromExe(@"C:\ProgramData\Package Cache\{GUID}\setup.exe"));
        Assert.Equal(@"C:\Program Files\Foo", WindowsProgramProvider.DeriveFromExe(@"""C:\Program Files\Foo\uninstall.exe"" /S"));
        Assert.Equal(@"C:\Program Files\Foo", WindowsProgramProvider.DeriveFromExe(@"C:\Program Files\Foo\foo.exe,0"));
        Assert.Null(WindowsProgramProvider.DeriveFromExe(@"""C:\Program Files\setup.exe"""));
    }
}
