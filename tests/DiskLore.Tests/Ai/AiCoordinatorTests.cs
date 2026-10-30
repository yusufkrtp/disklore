using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using Xunit;

namespace DiskLore.Tests.Ai;

public class AiCoordinatorTests
{
    private const long GB = 1L << 30;

    private static ScanNode Dir(ScanNode parent, string name, long size)
    {
        var d = new ScanNode(name, parent, true) { Size = size };
        parent.AddChild(d);
        return d;
    }

    private static ScanNode File(ScanNode parent, string name, long size)
    {
        var f = new ScanNode(name, parent, false) { Size = size };
        parent.AddChild(f);
        return f;
    }

    private static Explanation Known(ScanNode n) => new(n.Name, Badge.App, null, "s", "", "", "", null, null, ExplanationSource.Rule, 1, "r");
    private static Explanation Unknown(ScanNode n) => new(n.Name, Badge.Unknown, null, "", "", "", "", null, null, ExplanationSource.None, 0, null);

    private static AiCoordinator Make(Func<ScanNode, Explanation> explain)
        => new(new ClaudeCliExplainer(new AiCache(Path.Combine(Path.GetTempPath(), "unused-cache.json"))), explain, _ => { });

    [Fact]
    public void Selects_unknown_nodes_at_or_above_threshold_largest_first()
    {
        var root = new ScanNode(Path.GetFullPath("scanroot"), null, true) { Size = 100 * GB };
        var a = Dir(root, "A", 40 * GB);
        var b = Dir(root, "B", 30 * GB);
        var small = Dir(root, "small", GB / 2);
        var aInner = Dir(a, "inner", 5 * GB);
        File(a, "big.bin", 3 * GB);
        Dir(small, "hidden", GB / 4);

        var picked = Make(Unknown).SelectCandidates(root, GB, 10);
        Assert.Equal(new[] { a, b, aInner }, picked.Where(n => n.IsDirectory).ToArray());
        Assert.Equal(new long[] { 40 * GB, 30 * GB, 5 * GB, 3 * GB }, picked.Select(n => n.Size).ToArray());
        Assert.DoesNotContain(root, picked);
        Assert.DoesNotContain(small, picked);
    }

    [Fact]
    public void Skips_explained_reparse_and_access_denied_and_honors_max()
    {
        var root = new ScanNode(Path.GetFullPath("scanroot"), null, true) { Size = 100 * GB };
        var explained = Dir(root, "Explained", 50 * GB);
        var link = Dir(root, "Link", 20 * GB);
        link.IsReparsePoint = true;
        var denied = Dir(root, "Denied", 15 * GB);
        denied.AccessDenied = true;
        var x = Dir(root, "X", 10 * GB);
        var y = Dir(root, "Y", 9 * GB);
        var inner = Dir(explained, "Inner", 8 * GB);

        var coord = Make(n => ReferenceEquals(n, explained) ? Known(n) : Unknown(n));
        var picked = coord.SelectCandidates(root, GB, 10);
        Assert.DoesNotContain(explained, picked);
        Assert.DoesNotContain(link, picked);
        Assert.DoesNotContain(denied, picked);
        Assert.Equal(new[] { x, y, inner }, picked.ToArray());

        Assert.Equal(new[] { x }, coord.SelectCandidates(root, GB, 1).ToArray());
        Assert.Empty(coord.SelectCandidates(root, GB, 0));
    }

    [Fact]
    public void SampleNames_caps_at_30_and_prefers_large_descendants()
    {
        var root = new ScanNode(Path.GetFullPath("scanroot"), null, true);
        var d = Dir(root, "D", 0);
        for (var i = 0; i < 100; i++) File(d, $"f{i:000}.dat", 1000 - i);
        var sub = Dir(d, "sub", 5000);
        File(sub, "huge.bin", 5000);
        d.Size = 100000;
        d.SortChildrenBySizeDescendingForTest();

        var names = AiCoordinator.SampleNames(d);
        Assert.InRange(names.Count, 20, 30);
        Assert.Contains("huge.bin", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void BuildItem_falls_back_to_direct_children_and_sets_parent_title()
    {
        var root = new ScanNode(Path.GetFullPath("scanroot"), null, true) { Size = 10 * GB };
        var parent = Dir(root, "Parent", 9 * GB);
        var child = Dir(parent, "Child", 8 * GB);
        File(child, "a.bin", 5 * GB);
        File(child, "b.bin", 3 * GB);

        var coord = Make(n => ReferenceEquals(n, parent) ? Known(n) with { Title = "Parent title" } : Unknown(n));
        var item = coord.BuildItem(child);
        Assert.Equal("Parent title", item.ParentTitle);
        Assert.Null(item.InstalledProgramMatch);
        Assert.Equal(8 * GB, item.Size);
        Assert.NotEmpty(item.TopChildren);
        Assert.Equal("a.bin", item.TopChildren[0].Name);
    }
}

internal static class ScanNodeTestExtensions
{
    public static void SortChildrenBySizeDescendingForTest(this ScanNode n) => n.SortChildrenBySizeDescending();
}
