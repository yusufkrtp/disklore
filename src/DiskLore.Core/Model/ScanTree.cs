namespace DiskLore.Core.Model;

public static class ScanTree
{
    // Detaches node from its parent and subtracts Size/FileCount/FolderCount up the chain.
    // A directory's FolderCount excludes itself, so removing it also drops one folder.
    public static void Remove(ScanNode node)
    {
        var parent = node.Parent;
        if (parent == null) return;

        long size = node.Size;
        long files = node.FileCount;
        long folders = node.IsDirectory ? node.FolderCount + 1 : 0;

        parent.RemoveChild(node);
        node.Parent = null;

        for (var p = parent; p != null; p = p.Parent)
        {
            p.Size -= size;
            p.FileCount -= files;
            p.FolderCount -= folders;
        }
    }
}
