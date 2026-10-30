namespace DiskLore.Core.Model;

public sealed class ScanNode
{
    private readonly List<ScanNode> _children = new();

    internal ScanNode(string name, ScanNode? parent, bool isDirectory)
    {
        Name = name;
        Parent = parent;
        IsDirectory = isDirectory;
        if (!isDirectory) FileCount = 1;
    }

    public string Name { get; }
    public ScanNode? Parent { get; internal set; }
    public bool IsDirectory { get; }
    public bool IsReparsePoint { get; internal set; }
    public string? LinkTarget { get; internal set; }
    public long Size { get; internal set; }
    public long FileCount { get; internal set; }
    public long FolderCount { get; internal set; }
    public DateTime LastWriteUtc { get; internal set; }
    public bool AccessDenied { get; internal set; }
    public IReadOnlyList<ScanNode> Children => _children;

    public string FullPath => Parent == null ? Name : Path.Combine(Parent.FullPath, Name);

    public string Extension => IsDirectory ? "" : Path.GetExtension(Name).ToLowerInvariant();

    public int Depth
    {
        get
        {
            int depth = 0;
            for (var p = Parent; p != null; p = p.Parent) depth++;
            return depth;
        }
    }

    internal void AddChild(ScanNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal bool RemoveChild(ScanNode child) => _children.Remove(child);

    internal void SortChildrenBySizeDescending()
    {
        _children.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var c in _children)
            if (c.IsDirectory) c.SortChildrenBySizeDescending();
    }

    public override string ToString() => FullPath;
}
