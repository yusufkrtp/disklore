using System.Diagnostics;
using DiskLore.App.Dialogs;
using DiskLore.App.Services;
using DiskLore.App.ViewModels;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Programs;
using DiskLore.Core.Scanning;
using DiskLore.Core.Snapshots;
using Xunit;

namespace DiskLore.Tests.AppTests;

// Acceptance-level checks for the App layer: delete flow logic, in-use check, snapshot diff, admin button.
public class PolishTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disklore-polish-" + Guid.NewGuid().ToString("N"));

    public PolishTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    private static async Task<ScanResult> ScanAsync(string path)
        => await new Scanner().ScanAsync(path, null, CancellationToken.None);

    private static ScanNode Child(ScanNode n, string name)
        => n.Children.Single(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private string MakeFixture()
    {
        var root = Path.Combine(_dir, "fixture");
        Directory.CreateDirectory(Path.Combine(root, "a", "sub"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        File.WriteAllBytes(Path.Combine(root, "a", "big.bin"), new byte[5 * 1024 * 1024]);
        File.WriteAllBytes(Path.Combine(root, "a", "sub", "small.txt"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "b", "keep.bin"), new byte[2048]);
        return root;
    }

    // ---- b: delete flow ------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteFlow_RemovesNodeAndUpdatesParentSizes(bool permanent)
    {
        var platform = new FakePlatform { AppData = Path.Combine(_dir, "appdata"), RealDelete = true };
        var root = MakeFixture();
        var result = await ScanAsync(root);
        var tree = new TreeModel();
        tree.Load(result.Root, n => new Explanation(n.Name, Badge.Check, null, "", "", "", "", null, null, ExplanationSource.None, 0, null));

        var before = result.Root.Size;
        var a = Child(result.Root, "a");
        var aSize = a.Size;
        Assert.True(aSize >= 5 * 1024 * 1024);
        var rowsBefore = tree.Rows.Count;

        var r = DeleteFlow.DeleteOnDisk(platform, a.FullPath, permanent);
        Assert.True(r.Success, r.Error);
        Assert.False(Directory.Exists(Path.Combine(root, "a")));

        DeleteFlow.Commit(platform, a, permanent, aSize, Badge.Check);
        tree.RefreshAfterChange();

        Assert.Equal(before - aSize, result.Root.Size);
        Assert.DoesNotContain(result.Root.Children, c => ReferenceEquals(c, a));
        Assert.Single(result.Root.Children);   // only "b" is left
        Assert.Equal(1, result.Root.FileCount);
        Assert.Equal(rowsBefore - 1, tree.Rows.Count);

        var log = File.ReadAllLines(Path.Combine(platform.AppDataDirectory, "deletions.log")).Last();
        Assert.Contains(permanent ? "permanent" : "trash", log);
        Assert.EndsWith(Path.Combine(root, "a"), log);
    }

    [Fact]
    public void CriticalDialog_ButtonNeedsExactName()
    {
        Assert.False(CriticalDeleteDialog.NameMatches("", "WinSxS"));
        Assert.False(CriticalDeleteDialog.NameMatches(null, "WinSxS"));
        Assert.False(CriticalDeleteDialog.NameMatches("winsxs", "WinSxS"));
        Assert.False(CriticalDeleteDialog.NameMatches("WinSxS ", "WinSxS"));
        Assert.False(CriticalDeleteDialog.NameMatches("WinSx", "WinSxS"));
        Assert.True(CriticalDeleteDialog.NameMatches("WinSxS", "WinSxS"));
    }

    // ---- c: in-use check -----------------------------------------------------------------------

    [Fact]
    public async Task CheckInUse_NamesTheProcessHoldingAFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = MakeFixture();
        var held = Path.Combine(root, "a", "big.bin");
        var ps = Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -Command \"$f=[IO.File]::Open('{held}','Open','ReadWrite','None'); Start-Sleep 90\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            // Wait until the helper really holds the file.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try { using var _ = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None); }
                catch (IOException) { break; }
                await Task.Delay(200);
            }

            var result = await ScanAsync(root);
            var platform = PlatformServices.Create();
            var lines = DetailViewModel.FindLockers(platform, Child(result.Root, "a"));
            Assert.Contains(lines, l => l.Contains($"PID {ps.Id}", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("powershell", StringComparison.OrdinalIgnoreCase));

            // Same call on the file itself.
            var file = DetailViewModel.FindLockers(platform, Child(Child(result.Root, "a"), "big.bin"));
            Assert.Contains(file, l => l.Contains($"PID {ps.Id}", StringComparison.Ordinal));
        }
        finally
        {
            try { ps.Kill(true); } catch (Exception) { }
            ps.WaitForExit(5000);
        }
    }

    // ---- d: snapshot diff ----------------------------------------------------------------------

    [Fact]
    public async Task Snapshot_OfAFolderRoot_DiffShowsOneGigabyteGrowth()
    {
        var root = MakeFixture();
        var store = new SnapshotStore(Path.Combine(_dir, "snaps"));

        var first = await ScanAsync(root);
        Assert.False(first.IsDriveRoot);
        store.Save(first);
        await Task.Delay(1100);   // snapshot names carry seconds

        var big = Path.Combine(root, "b", "dummy.bin");
        using (var fs = new FileStream(big, FileMode.Create, FileAccess.Write))
            fs.SetLength(1L << 30);

        var second = await ScanAsync(root);
        var list = store.List(second.Root.FullPath);
        Assert.Single(list);
        var diff = SnapshotDiff.Compare(store.Load(list[0]), second.Root);

        var b = diff.Single(d => d.RelativePath == "b");
        Assert.InRange(b.Delta, (1L << 30) - 1_000_000, (1L << 30) + 1_000_000);
    }

    // ---- f: admin button -----------------------------------------------------------------------

    private sealed class FakePlatform : IPlatformServices
    {
        private readonly IPlatformServices _real = PlatformServices.Create();
        public bool Elevated;
        public int RestartCalls;
        public string? AppData;      // keeps deletions.log out of the real data folder
        public bool RealDelete;
        public string AppDataDirectory => AppData ?? _real.AppDataDirectory;
        public IReadOnlyList<DriveEntry> GetDrives() => _real.GetDrives();
        public IInstalledProgramProvider Programs => _real.Programs;
        public bool SupportsLockDetection => false;
        public IReadOnlyList<LockingProcess> FindLockingProcesses(IReadOnlyList<string> filePaths) => [];
        public DeleteResult MoveToTrash(string path) => RealDelete ? _real.MoveToTrash(path) : new(true, null);
        public DeleteResult DeletePermanently(string path) => RealDelete ? _real.DeletePermanently(path) : new(true, null);
        public void RevealInFileManager(string path) { }
        public void OpenUri(string uri) { }
        public void RunCommandElevated(string commandLine) { }
        public bool SupportsElevatedCommands => false;
        public bool IsElevated => Elevated;
        public void RestartElevated(string? scanPath)
        {
            RestartCalls++;
            throw new OperationCanceledException("UAC declined");
        }
        public bool SupportsShellIntegration => false;
        public bool IsShellIntegrationEnabled() => false;
        public void SetShellIntegration(bool enabled, string exePath) { }
    }

    private static ScanResult TinyResult()
    {
        var root = TestTree.Root(TestTree.AbsRoot);
        TestTree.File(TestTree.Dir(root, "a"), "x.bin", 10);
        TestTree.Aggregate(root);
        return new ScanResult(root, DateTime.UtcNow, TimeSpan.Zero, [], false);
    }

    [Fact]
    public void AdminButton_HiddenWhenElevated_VisibleOtherwise_AndDeclinedUacKeepsAppOpen()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fake = new FakePlatform();
        var vm = new MainViewModel { Platform = fake };

        Assert.False(vm.ShowAdminButton);   // nothing scanned yet
        vm.LoadResult(TinyResult());
        Assert.True(vm.ShowAdminButton);

        fake.Elevated = true;
        vm.RefreshStatus();
        Assert.False(vm.ShowAdminButton);
        Assert.True(vm.IsElevated);

        fake.Elevated = false;
        vm.RefreshStatus();
        Assert.True(vm.ShowAdminButton);

        // A declined UAC prompt throws OperationCanceledException: the command must swallow it and stay open.
        vm.RestartAsAdminCommand.Execute(null);
        Assert.Equal(1, fake.RestartCalls);
        Assert.Equal("Administrator restart was cancelled", vm.StatusText);
        Assert.True(vm.HasResult);
    }

    // ---- misc ----------------------------------------------------------------------------------

    [Fact]
    public void AiStatusTexts()
    {
        Assert.Equal("Claude Code explained 1 item", MainViewModel.ExplainedText(1, "Claude Code"));
        Assert.Equal("Codex CLI explained 8 items", MainViewModel.ExplainedText(8, "Codex CLI"));
        Assert.Equal("Claude Code unavailable: boom", MainViewModel.UnavailableText(new InvalidOperationException("boom\nsecond line"), "Claude Code"));
        Assert.True(MainViewModel.UnavailableText(new TimeoutException(new string((char)120, 300)), "Ollama").Length < 110);
    }

    [Fact]
    public void BreakablePath_KeepsDriveTogetherAndBreaksAtSeparators()
    {
        var s = DetailViewModel.BreakablePath("C:\\Users\\Alice\\c--Temp");
        Assert.Equal("C:\\Users\\Alice\\c--Temp", s.Replace("\u2060", "").Replace("\u200B", ""));
        Assert.Contains(":\u2060\\\u200B", s);
        Assert.Contains("\\\u200BAlice", s);
        Assert.Contains("-\u2060-\u2060Temp", s);
    }
}
