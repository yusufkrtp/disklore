using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.App.Converters;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.ViewModels;

public static class SearchEngine
{
    // Name contains query (case-insensitive), the largest `max` matches, largest first. The root itself is skipped.
    public static IReadOnlyList<ScanNode> Find(ScanNode root, string query, int max, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0 || max <= 0) return Array.Empty<ScanNode>();

        // Min-heap on size keeps only the biggest `max` matches.
        var heap = new PriorityQueue<ScanNode, long>();
        var stack = new Stack<ScanNode>();
        foreach (var c in root.Children) stack.Push(c);
        var visited = 0;
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if ((++visited & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            if (n.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (heap.Count < max) heap.Enqueue(n, n.Size);
                else if (heap.TryPeek(out _, out var smallest) && n.Size > smallest)
                {
                    heap.Dequeue();
                    heap.Enqueue(n, n.Size);
                }
            }
            if (n.IsDirectory && !n.IsReparsePoint)
                foreach (var c in n.Children) stack.Push(c);
        }

        var result = new List<ScanNode>(heap.Count);
        while (heap.TryDequeue(out var node, out _)) result.Add(node);
        result.Reverse();
        return result;
    }
}

public sealed class SearchResultViewModel
{
    private readonly Func<ScanNode, Explanation> _explain;
    private Explanation? _e;

    public SearchResultViewModel(ScanNode node, Func<ScanNode, Explanation> explain)
    {
        Node = node;
        _explain = explain;
    }

    public ScanNode Node { get; }
    public string Name => Node.Name;
    public string FullPath => Node.FullPath;
    public string SizeText => SizeFormat.Format(Node.Size);
    public bool IsFolder => Node.IsDirectory;

    private Explanation? E
    {
        get
        {
            if (_e == null)
            {
                try { _e = _explain(Node); } catch (Exception) { }
            }
            return _e;
        }
    }

    public Badge Badge => E?.Badge ?? Badge.Unknown;
    public string Summary => E?.Summary ?? "";
}
