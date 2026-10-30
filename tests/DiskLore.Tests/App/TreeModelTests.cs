using DiskLore.App.ViewModels;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using Xunit;

namespace DiskLore.Tests.AppTests;

public class TreeModelTests
{
    private static Explanation Unknown(ScanNode n)
        => new(n.Name, Badge.Unknown, null, "", "", "", "", null, null, ExplanationSource.None, 0, null);

    private static (ScanNode root, ScanNode a, ScanNode b) Build()
    {
        var root = TestTree.Root(TestTree.AbsRoot);
        var a = TestTree.Dir(root, "a");
        var b = TestTree.Dir(root, "b");
        TestTree.File(a, "a1.bin", 100);
        TestTree.File(a, "a2.bin", 50);
        TestTree.File(b, "b1.bin", 10);
        TestTree.Aggregate(root);
        return (root, a, b);
    }

    [Fact]
    public void RootStartsExpanded_ChildrenSizeDescending()
    {
        var (root, a, _) = Build();
        var m = new TreeModel();
        m.Load(root, Unknown);
        Assert.Equal(3, m.Rows.Count);
        Assert.Same(root, m.Rows[0].Node);
        Assert.Same(a, m.Rows[1].Node);
        Assert.Equal(1, m.Rows[1].Depth);
    }

    [Fact]
    public void ExpandAndCollapse_InsertAndRemoveChildren()
    {
        var (root, a, _) = Build();
        var m = new TreeModel();
        m.Load(root, Unknown);
        m.Expand(m.Rows[1]);
        Assert.Equal(5, m.Rows.Count);
        Assert.Equal(2, m.Rows[2].Depth);
        m.Collapse(m.Rows[1]);
        Assert.Equal(3, m.Rows.Count);
    }

    [Fact]
    public void Paging_ShowsFirstPageThenMoreRow()
    {
        var root = TestTree.Root(TestTree.AbsRoot);
        for (var i = 0; i < 25; i++) TestTree.File(root, $"f{i:00}.bin", 1000 - i);
        TestTree.Aggregate(root);
        var m = new TreeModel { PageSize = 10 };
        m.Load(root, Unknown);
        Assert.Equal(1 + 10 + 1, m.Rows.Count);
        Assert.True(m.Rows[^1].IsShowMore);
        Assert.Contains("15 items", m.Rows[^1].MoreText);
        m.ShowMore(m.Rows[^1]);
        Assert.Equal(1 + 20 + 1, m.Rows.Count);
        m.ShowMore(m.Rows[^1]);
        Assert.Equal(1 + 25, m.Rows.Count);
        Assert.False(m.Rows[^1].IsShowMore);
    }

    [Fact]
    public void ExpandTo_ExpandsAncestorsAndReturnsRow()
    {
        var (root, a, _) = Build();
        var leaf = a.Children[0];
        var m = new TreeModel();
        m.Load(root, Unknown);
        var row = m.ExpandTo(leaf);
        Assert.NotNull(row);
        Assert.Same(leaf, row!.Node);
        Assert.Contains(row, m.Rows);
    }

    [Fact]
    public void SortByName_Ascending()
    {
        var (root, a, b) = Build();
        var m = new TreeModel();
        m.Load(root, Unknown);
        m.SetSort(TreeSort.Name);
        Assert.Same(a, m.Rows[1].Node);
        m.SetSort(TreeSort.Name);
        Assert.Same(b, m.Rows[1].Node);
    }

    [Fact]
    public void Search_FindsByNameLargestFirst()
    {
        var (root, _, _) = Build();
        var found = SearchEngine.Find(root, "BIN", 2);
        Assert.Equal(2, found.Count);
        Assert.Equal("a1.bin", found[0].Name);
        Assert.Equal("a2.bin", found[1].Name);
    }
}
