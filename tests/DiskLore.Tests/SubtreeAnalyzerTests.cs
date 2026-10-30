using DiskLore.Core.Scanning;
using Xunit;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

public class SubtreeAnalyzerTests
{
    private static Core.Model.ScanNode Build()
    {
        var root = Root(AbsRoot);
        var a = Dir(root, "a");
        var b = Dir(a, "b");
        File(root, "top.MP4", 900, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File(a, "x.mp4", 100, new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File(b, "y.txt", 50, new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File(b, "README", 5, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File(b, "z.log", 70, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Aggregate(root);
        return root;
    }

    [Fact]
    public void Analyze_ReportsChildrenExtensionsLargestAndDates()
    {
        var root = Build();
        var stats = SubtreeAnalyzer.Analyze(root);

        Assert.Equal(5, stats.FileCount);
        Assert.Equal("top.MP4", stats.TopChildren[0].Name);
        Assert.Equal(new[] { ".mp4", ".log", ".txt", "(none)" }, stats.TopExtensions.Select(e => e.Name));
        Assert.Equal(1000, stats.TopExtensions[0].Size);
        Assert.Equal("top.MP4", stats.LargestFile!.Name);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), stats.OldestWriteUtc);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), stats.NewestWriteUtc);
    }

    [Fact]
    public void Analyze_LargestFileNameIsRelativeToNode()
    {
        var root = Build();
        var a = root.Children.Single(c => c.Name == "a");
        var stats = SubtreeAnalyzer.Analyze(a);
        Assert.Equal("x.mp4", stats.LargestFile!.Name);

        var b = a.Children.Single(c => c.Name == "b");
        Assert.Equal("z.log", SubtreeAnalyzer.Analyze(b).LargestFile!.Name);

        var r2 = Root(AbsRoot);
        File(Dir(Dir(r2, "p"), "q"), "big.bin", 10);
        Aggregate(r2);
        Assert.Equal(Path.Combine("p", "q", "big.bin"), SubtreeAnalyzer.Analyze(r2).LargestFile!.Name);
    }

    [Fact]
    public void Analyze_EmptyFolderHasNoDates()
    {
        var root = Root(AbsRoot);
        Dir(root, "empty");
        Aggregate(root);
        var stats = SubtreeAnalyzer.Analyze(root.Children[0]);
        Assert.Equal(0, stats.FileCount);
        Assert.Null(stats.LargestFile);
        Assert.Null(stats.OldestWriteUtc);
        Assert.Null(stats.NewestWriteUtc);
        Assert.Empty(stats.TopExtensions);
    }

    [Fact]
    public void ExtensionBreakdown_RespectsMax()
    {
        var all = SubtreeAnalyzer.ExtensionBreakdown(Build());
        Assert.Equal(4, all.Count);
        var two = SubtreeAnalyzer.ExtensionBreakdown(Build(), 2);
        Assert.Equal(new[] { ".mp4", ".log" }, two.Select(e => e.Name));
    }

    [Fact]
    public void LargestFiles_ReturnsTopNDescending()
    {
        var top = SubtreeAnalyzer.LargestFiles(Build(), 3);
        Assert.Equal(new[] { "top.MP4", "x.mp4", "z.log" }, top.Select(n => n.Name));
        Assert.Equal(5, SubtreeAnalyzer.LargestFiles(Build(), 100).Count);
    }

    [Fact]
    public void LargestFiles_HandlesVeryDeepTrees()
    {
        var root = Root(AbsRoot);
        var cur = root;
        for (int i = 0; i < 20_000; i++) cur = Dir(cur, "d");
        File(cur, "deep.bin", 7);
        Aggregate(root);
        Assert.Equal("deep.bin", SubtreeAnalyzer.LargestFiles(root, 1)[0].Name);
        Assert.Equal(7, SubtreeAnalyzer.Analyze(root).LargestFile!.Size);
    }
}
