using DiskLore.Core.Model;

namespace DiskLore.Core.Scanning;

public static class SubtreeAnalyzer
{
    private const string NoExtension = "(none)";

    public static SubtreeStats Analyze(ScanNode node)
    {
        var topChildren = TopChildrenOf(node, 5);

        var ext = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var extLookup = ext.GetAlternateLookup<ReadOnlySpan<char>>();
        ScanNode? largest = null;
        DateTime? oldest = null, newest = null;
        long files = 0;

        foreach (var f in Files(node))
        {
            files++;
            AddExtension(extLookup, f);
            if (largest == null || f.Size > largest.Size) largest = f;
            var w = f.LastWriteUtc;
            if (oldest == null || w < oldest) oldest = w;
            if (newest == null || w > newest) newest = w;
        }

        NamedSize? largestFile = largest == null ? null : new NamedSize(RelativePath(node, largest), largest.Size);
        return new SubtreeStats(topChildren, TopExtensionsOf(ext, 5), files, largestFile, oldest, newest);
    }

    public static IReadOnlyList<NamedSize> ExtensionBreakdown(ScanNode node, int max = 50)
    {
        var ext = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var lookup = ext.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var f in Files(node)) AddExtension(lookup, f);
        return TopExtensionsOf(ext, max);
    }

    public static IReadOnlyList<ScanNode> LargestFiles(ScanNode root, int count = 100)
    {
        if (count <= 0) return Array.Empty<ScanNode>();
        // Min-heap on size: the smallest kept file sits on top and is evicted first.
        var heap = new PriorityQueue<ScanNode, long>();
        foreach (var f in Files(root))
        {
            if (heap.Count < count) heap.Enqueue(f, f.Size);
            else if (heap.TryPeek(out _, out var min) && f.Size > min) heap.EnqueueDequeue(f, f.Size);
        }

        var result = new ScanNode[heap.Count];
        for (int i = result.Length - 1; i >= 0; i--) result[i] = heap.Dequeue();
        return result;
    }

    private static void AddExtension(Dictionary<string, long>.AlternateLookup<ReadOnlySpan<char>> lookup, ScanNode file)
    {
        var e = Path.GetExtension(file.Name.AsSpan());
        if (e.Length <= 1) e = NoExtension; // "" or a lone "."
        lookup[e] = (lookup.TryGetValue(e, out var cur) ? cur : 0) + file.Size;
    }

    private static List<NamedSize> TopExtensionsOf(Dictionary<string, long> ext, int max)
    {
        var list = new List<NamedSize>(ext.Count);
        foreach (var (k, v) in ext)
            list.Add(new NamedSize(k == NoExtension ? k : k.ToLowerInvariant(), v));
        list.Sort(static (a, b) =>
        {
            int c = b.Size.CompareTo(a.Size);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return list;
    }

    private static List<NamedSize> TopChildrenOf(ScanNode node, int max)
    {
        // Children are sorted after a scan, but pick explicitly so a stale order never leaks out.
        var top = new List<ScanNode>(max + 1);
        foreach (var c in node.Children)
        {
            if (top.Count == max && c.Size <= top[^1].Size) continue;
            int i = top.Count;
            while (i > 0 && top[i - 1].Size < c.Size) i--;
            top.Insert(i, c);
            if (top.Count > max) top.RemoveAt(top.Count - 1);
        }
        return top.Select(c => new NamedSize(c.Name, c.Size)).ToList();
    }

    private static IEnumerable<ScanNode> Files(ScanNode root)
    {
        if (!root.IsDirectory)
        {
            yield return root;
            yield break;
        }
        var stack = new Stack<ScanNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            foreach (var c in d.Children)
            {
                if (c.IsDirectory) stack.Push(c);
                else yield return c;
            }
        }
    }

    private static string RelativePath(ScanNode ancestor, ScanNode file)
    {
        if (ReferenceEquals(ancestor, file)) return file.Name;
        var parts = new List<string>();
        for (var n = file; n != null && !ReferenceEquals(n, ancestor); n = n.Parent) parts.Add(n.Name);
        parts.Reverse();
        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
