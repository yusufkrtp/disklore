using DiskLore.Core.Model;
using DiskLore.Core.Snapshots;
using Xunit;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

public sealed class SnapshotTests : IDisposable
{
    private const long MB = 1024 * 1024;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disklore-snap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ScanNode Tree(long big, long small = 10 * MB, bool withNew = false, bool withOld = true)
    {
        var root = Root(AbsRoot);
        var data = Dir(root, "data");
        File(data, "big.bin", big);
        File(data, "tiny.txt", 100);
        if (withOld) File(Dir(root, "old"), "o.bin", 200 * MB);
        File(Dir(root, "stable"), "s.bin", small);
        if (withNew) File(Dir(root, "fresh"), "f.bin", 300 * MB);
        Aggregate(root);
        return root;
    }

    private static ScanResult Result(ScanNode root, DateTime? started = null) =>
        new(root, started ?? new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1), Array.Empty<string>(), false);

    [Fact]
    public void SaveListLoad_RoundTrips()
    {
        var store = new SnapshotStore(_dir);
        var root = Tree(100 * MB);
        var info = store.Save(Result(root));

        Assert.True(File2.Exists(info.FilePath));
        Assert.EndsWith(".dlsnap", info.FilePath);
        Assert.Equal(root.Name, info.RootPath);
        Assert.Equal(root.Size, info.TotalSize);

        var listed = store.List(root.Name);
        Assert.Single(listed);
        Assert.Equal(info, listed[0]);
        Assert.Empty(store.List(Path.Combine(Path.GetTempPath(), "other-root")));

        var snap = store.Load(listed[0]);
        Assert.Equal(root.Name, snap.RootPath);
        Assert.Equal(info.TakenUtc, snap.TakenUtc);
        Assert.Equal(root.Size, snap.Sizes[""]);
        Assert.Equal(100 * MB + 100, snap.Sizes["data"]);
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(100 * MB, snap.Sizes["data" + sep + "big.bin"]);
        Assert.False(snap.Sizes.ContainsKey("data" + sep + "tiny.txt")); // under 50 MB
        Assert.False(snap.Sizes.ContainsKey("stable" + sep + "s.bin"));   // 10 MB
        Assert.True(snap.Sizes.ContainsKey("stable"));
    }

    [Fact]
    public void FileName_UsesSanitizedRootAndTimestamp()
    {
        var store = new SnapshotStore(_dir);
        var info = store.Save(Result(Root(OperatingSystem.IsWindows() ? @"C:\" : "/"), new DateTime(2026, 10, 30, 13, 5, 9, DateTimeKind.Utc)));
        var name = Path.GetFileName(info.FilePath);
        Assert.EndsWith("-20261030-130509.dlsnap", name);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        if (OperatingSystem.IsWindows()) Assert.Equal("C_-20261030-130509.dlsnap", name);
    }

    [Fact]
    public void Save_KeepsNewestTenPerRoot()
    {
        var store = new SnapshotStore(_dir);
        var root = Tree(100 * MB);
        var other = Tree(100 * MB);
        var otherRoot = Root(Path.Combine(AbsRoot, "elsewhere"));
        otherRoot.Size = 1;

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 13; i++) store.Save(Result(root, start.AddDays(i)));
        store.Save(Result(otherRoot, start));

        var list = store.List(root.Name);
        Assert.Equal(10, list.Count);
        Assert.Equal(start.AddDays(12), list[0].TakenUtc);
        Assert.Equal(start.AddDays(3), list[^1].TakenUtc);
        Assert.Single(store.List(otherRoot.Name));
        Assert.NotNull(other);
    }

    [Fact]
    public void Save_TwoInTheSameSecondDoNotCollide()
    {
        var store = new SnapshotStore(_dir);
        var root = Tree(100 * MB);
        var a = store.Save(Result(root));
        var b = store.Save(Result(root));
        Assert.NotEqual(a.FilePath, b.FilePath);
        Assert.Equal(2, store.List(root.Name).Count);
    }

    [Fact]
    public void List_SkipsCorruptFiles()
    {
        var store = new SnapshotStore(_dir);
        var root = Tree(100 * MB);
        store.Save(Result(root));
        System.IO.File.WriteAllText(Path.Combine(_dir, "junk.dlsnap"), "not gzip");
        Assert.Single(store.List(root.Name));
    }

    [Fact]
    public void Diff_ReportsGrownNewAndRemovedFolders()
    {
        var store = new SnapshotStore(_dir);
        var older = Tree(100 * MB, withOld: true);
        var snap = store.Load(store.Save(Result(older)));

        var current = Tree(1124 * MB, withNew: true, withOld: false); // data +1 GB, fresh new, old removed
        var diff = SnapshotDiff.Compare(snap, current);

        var byPath = diff.ToDictionary(d => d.RelativePath);
        Assert.Equal(1024 * MB, byPath["data"].Delta);
        Assert.Equal(0, byPath["fresh"].OldSize);
        Assert.Equal(300 * MB, byPath["fresh"].NewSize);
        Assert.Equal(200 * MB, byPath["old"].OldSize);
        Assert.Equal(0, byPath["old"].NewSize);
        Assert.False(byPath.ContainsKey("stable"));
        Assert.False(byPath.ContainsKey(""));
        Assert.DoesNotContain(diff, d => d.RelativePath.EndsWith("big.bin")); // folders only

        Assert.Equal(new[] { "data", "fresh", "old" }, diff.Select(d => d.RelativePath));
    }

    [Fact]
    public void Diff_MinDeltaFilters()
    {
        var older = new Snapshot(AbsRoot, DateTime.UtcNow, new Dictionary<string, long> { ["data"] = 100 * MB, ["small"] = 1 * MB });
        var current = Root(AbsRoot);
        File(Dir(current, "data"), "a", 160 * MB);
        File(Dir(current, "small"), "b", 2 * MB);
        Aggregate(current);

        Assert.Equal(new[] { "data" }, SnapshotDiff.Compare(older, current).Select(d => d.RelativePath));
        Assert.Equal(2, SnapshotDiff.Compare(older, current, 1 * MB).Count);
        Assert.Empty(SnapshotDiff.Compare(older, current, 1024 * MB));
    }

    [Fact]
    public void Diff_RemovedFilesAreNotReportedAsFolders()
    {
        var store = new SnapshotStore(_dir);
        var older = Root(AbsRoot);
        File(Dir(older, "d"), "huge.bin", 500 * MB);
        Aggregate(older);
        var snap = store.Load(store.Save(Result(older)));

        var current = Root(AbsRoot);
        Dir(current, "d");
        Aggregate(current);

        var diff = SnapshotDiff.Compare(snap, current);
        Assert.Single(diff);
        Assert.Equal("d", diff[0].RelativePath);
        Assert.Equal(-500 * MB, diff[0].Delta);
    }

    private static class File2
    {
        public static bool Exists(string p) => System.IO.File.Exists(p);
    }
}
