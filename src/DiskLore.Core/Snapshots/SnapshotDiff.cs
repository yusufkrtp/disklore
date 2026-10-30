using DiskLore.Core.Model;

namespace DiskLore.Core.Snapshots;

public static class SnapshotDiff
{
    // Folders only, |Delta| >= minDelta, sorted by Delta descending. New and removed folders included.
    // The root itself ("") is left out: it is the sum of everything else.
    public static IReadOnlyList<DiffEntry> Compare(Snapshot older, ScanNode currentRoot, long minDelta = 50L * 1024 * 1024)
    {
        var result = new List<DiffEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var stack = new Stack<(ScanNode Node, string Rel)>();
        stack.Push((currentRoot, ""));
        while (stack.Count > 0)
        {
            var (node, rel) = stack.Pop();
            seen.Add(rel);

            if (rel.Length > 0)
            {
                long old = older.Sizes.TryGetValue(rel, out var o) && IsFolder(older, rel) ? o : 0;
                Add(result, rel, old, node.Size, minDelta);
            }

            foreach (var c in node.Children)
            {
                if (!c.IsDirectory) continue;
                stack.Push((c, rel.Length == 0 ? c.Name : rel + Path.DirectorySeparatorChar + c.Name));
            }
        }

        // Folders that existed before and are gone now.
        foreach (var (key, size) in older.Sizes)
        {
            if (key.Length == 0 || seen.Contains(key)) continue;
            if (older.Folders != null && !older.Folders.Contains(key)) continue;
            Add(result, key, size, 0, minDelta);
        }

        result.Sort(static (a, b) =>
        {
            int c = b.Delta.CompareTo(a.Delta);
            return c != 0 ? c : string.CompareOrdinal(a.RelativePath, b.RelativePath);
        });
        return result;
    }

    private static bool IsFolder(Snapshot s, string key) => s.Folders == null || s.Folders.Contains(key);

    private static void Add(List<DiffEntry> list, string rel, long oldSize, long newSize, long minDelta)
    {
        long delta = newSize - oldSize;
        if (Math.Abs(delta) >= minDelta) list.Add(new DiffEntry(rel, oldSize, newSize));
    }
}
