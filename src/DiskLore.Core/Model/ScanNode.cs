namespace DiskLore.Core.Model;

public sealed class ScanNode
{
    private static readonly ScanNode[] NoChildren = Array.Empty<ScanNode>();

    // Files never allocate a list; directories allocate on first child.
    private List<ScanNode>? _children;

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
    public IReadOnlyList<ScanNode> Children => (IReadOnlyList<ScanNode>?)_children ?? NoChildren;

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
        (_children ??= new List<ScanNode>()).Add(child);
    }

    internal bool RemoveChild(ScanNode child) => _children != null && _children.Remove(child);

    // Size descending, name as a stable tie-break. Non-recursive: trees can be very deep.
    internal void SortChildren()
    {
        _children?.Sort(static (a, b) =>
        {
            int c = b.Size.CompareTo(a.Size);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
    }

    internal void SortChildrenBySizeDescending()
    {
        var stack = new Stack<ScanNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            n.SortChildren();
            if (n._children == null) continue;
            foreach (var c in n._children)
                if (c.IsDirectory) stack.Push(c);
        }
    }

    public override string ToString() => FullPath;
}
