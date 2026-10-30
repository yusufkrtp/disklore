using DiskLore.Core.Model;
using Xunit;

namespace DiskLore.Tests;

public class ScanNodeTests
{
    private static ScanNode File(ScanNode parent, string name, long size)
    {
        var f = new ScanNode(name, parent, false) { Size = size };
        parent.AddChild(f);
        return f;
    }

    [Fact]
    public void FullPath_JoinsParentsAndExtensionIsLowerCase()
    {
        var root = new ScanNode(Path.GetFullPath("root"), null, true);
        var dir = new ScanNode("sub", root, true);
        root.AddChild(dir);
        var f = File(dir, "Movie.MKV", 10);

        Assert.Equal(Path.Combine(root.Name, "sub", "Movie.MKV"), f.FullPath);
        Assert.Equal(".mkv", f.Extension);
        Assert.Equal("", dir.Extension);
        Assert.Equal(2, f.Depth);
    }

    [Fact]
    public void Remove_SubtractsSizesUpTheChain()
    {
        var root = new ScanNode(Path.GetFullPath("root"), null, true);
        var dir = new ScanNode("sub", root, true);
        root.AddChild(dir);
        var a = File(dir, "a.bin", 100);
        var b = File(dir, "b.bin", 40);
        File(root, "c.bin", 5);

        dir.Size = 140; dir.FileCount = 2;
        root.Size = 145; root.FileCount = 3; root.FolderCount = 1;

        ScanTree.Remove(a);

        Assert.Equal(40, dir.Size);
        Assert.Equal(1, dir.FileCount);
        Assert.Equal(45, root.Size);
        Assert.Equal(2, root.FileCount);
        Assert.DoesNotContain(a, dir.Children);
        Assert.Null(a.Parent);

        ScanTree.Remove(dir);

        Assert.Equal(5, root.Size);
        Assert.Equal(1, root.FileCount);
        Assert.Equal(0, root.FolderCount);
        Assert.Single(root.Children);
        Assert.NotNull(b);
    }
}
