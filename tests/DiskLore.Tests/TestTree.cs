using DiskLore.Core.Model;

namespace DiskLore.Tests;

// Builds in-memory trees without touching the disk.
internal static class TestTree
{
    public static ScanNode Root(string path) => new(path, null, true);

    public static ScanNode Dir(ScanNode parent, string name)
    {
        var d = new ScanNode(name, parent, true);
        parent.AddChild(d);
        return d;
    }

    public static ScanNode File(ScanNode parent, string name, long size, DateTime? written = null)
    {
        var f = new ScanNode(name, parent, false) { Size = size, LastWriteUtc = written ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        parent.AddChild(f);
        return f;
    }

    // Walks the tree after construction and fills the aggregates like the scanner does.
    public static void Aggregate(ScanNode root)
    {
        var dirs = new List<ScanNode>();
        var stack = new Stack<ScanNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            dirs.Add(d);
            foreach (var c in d.Children) if (c.IsDirectory) stack.Push(c);
        }
        for (int i = dirs.Count - 1; i >= 0; i--)
        {
            var d = dirs[i];
            long size = 0, files = 0, folders = 0;
            foreach (var c in d.Children)
            {
                size += c.Size;
                files += c.FileCount;
                if (c.IsDirectory) folders += c.FolderCount + 1;
                if (c.LastWriteUtc > d.LastWriteUtc) d.LastWriteUtc = c.LastWriteUtc;
            }
            d.Size = size;
            d.FileCount = files;
            d.FolderCount = folders;
            d.SortChildren();
        }
    }

    public static string AbsRoot => OperatingSystem.IsWindows() ? @"C:\Fixture" : "/fixture";
}
