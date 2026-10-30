using Avalonia;
using Avalonia.Media;
using DiskLore.Core.Model;

namespace DiskLore.App.Controls;

// One rectangle of the treemap. Parents come before their children in the list.
public sealed class TreemapCell
{
    public TreemapCell(ScanNode node, Rect rect, int depth)
    {
        Node = node;
        Rect = rect;
        Depth = depth;
    }

    public ScanNode Node { get; }
    public Rect Rect { get; }
    public int Depth { get; }
    public bool IsGroup { get; internal set; }       // children are drawn inside this cell
    public bool HasHeader { get; internal set; }     // 14 px header strip with the folder name
    public Rect HeaderRect => new(Rect.X + TreemapLayout.Padding, Rect.Y + TreemapLayout.Padding,
        Math.Max(0, Rect.Width - 2 * TreemapLayout.Padding), TreemapLayout.HeaderHeight);

    // Render caches (filled lazily by the control, dropped on relayout).
    internal int BadgeIndex = -1;
    internal int TypeIndex = -1;
    internal FormattedText? Label;
    internal bool LabelBuilt;
    internal Color LabelColor;
}

// Squarified treemap layout (Bruls, Huizing, van Wijk), kept free of UI state so it can be tested.
public static class TreemapLayout
{
    public const double Padding = 2;
    public const double HeaderHeight = 14;
    public const double MinSide = 3;
    private const double MinHeaderWidth = 70;
    private const double MinHeaderCellHeight = 36;

    // Lays out `sizes` (largest first for the best aspect ratios) inside `bounds`.
    // The returned rectangles are in input order and their areas are proportional to the sizes.
    public static Rect[] Squarify(IReadOnlyList<long> sizes, Rect bounds)
    {
        var n = sizes.Count;
        var result = new Rect[n];
        double total = 0;
        for (var i = 0; i < n; i++) total += Math.Max(0, sizes[i]);
        if (n == 0 || total <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return result;

        var scale = bounds.Width * bounds.Height / total;
        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        var idx = 0;
        while (idx < n)
        {
            if (sizes[idx] <= 0)
            {
                result[idx] = new Rect(x, y, 0, 0);
                idx++;
                continue;
            }

            var side = Math.Min(w, h);
            double rowSum = 0, rowMin = double.MaxValue, rowMax = 0, worst = double.MaxValue;
            var j = idx;
            while (j < n && sizes[j] > 0)
            {
                var area = sizes[j] * scale;
                var newSum = rowSum + area;
                var newMin = Math.Min(rowMin, area);
                var newMax = Math.Max(rowMax, area);
                var newWorst = Worst(newMin, newMax, newSum, side);
                if (j > idx && newWorst > worst) break;
                rowSum = newSum;
                rowMin = newMin;
                rowMax = newMax;
                worst = newWorst;
                j++;
            }

            if (w >= h)
            {
                // Row is a column on the left edge.
                var colW = Math.Min(w, rowSum / h);
                var cy = y;
                for (var k = idx; k < j; k++)
                {
                    var hk = k == j - 1 ? y + h - cy : sizes[k] * scale / colW;
                    result[k] = new Rect(x, cy, colW, Math.Max(0, hk));
                    cy += hk;
                }
                x += colW;
                w -= colW;
            }
            else
            {
                // Row is a strip along the top edge.
                var rowH = Math.Min(h, rowSum / w);
                var cx = x;
                for (var k = idx; k < j; k++)
                {
                    var wk = k == j - 1 ? x + w - cx : sizes[k] * scale / rowH;
                    result[k] = new Rect(cx, y, Math.Max(0, wk), rowH);
                    cx += wk;
                }
                y += rowH;
                h -= rowH;
            }
            idx = j;
        }
        return result;
    }

    private static double Worst(double min, double max, double sum, double side)
    {
        var s2 = side * side;
        var sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }

    // Lays out the nodes that will actually be drawn: the zoom root, then children up to `maxDepth` levels.
    // Children whose rectangle would be smaller than MinSide x MinSide are not laid out at all (the tail of a folder
    // is one invisible lump, so the remaining rectangles keep their proportions).
    public static List<TreemapCell> Build(ScanNode zoomRoot, Rect bounds, int maxDepth = 3)
    {
        var cells = new List<TreemapCell>();
        if (bounds.Width < MinSide || bounds.Height < MinSide || zoomRoot.Size <= 0) return cells;
        var rootCell = new TreemapCell(zoomRoot, bounds, 0);
        cells.Add(rootCell);
        Recurse(rootCell, maxDepth, cells);
        return cells;
    }

    private static void Recurse(TreemapCell cell, int maxDepth, List<TreemapCell> cells)
    {
        var node = cell.Node;
        if (!node.IsDirectory || cell.Depth >= maxDepth || node.Children.Count == 0 || node.Size <= 0) return;

        var rect = cell.Rect;
        var inner = rect;
        var header = false;
        if (cell.Depth > 0)
        {
            header = rect.Width >= MinHeaderWidth && rect.Height >= MinHeaderCellHeight;
            inner = rect.Deflate(Padding);
            if (header) inner = new Rect(inner.X, inner.Y + HeaderHeight, inner.Width, inner.Height - HeaderHeight);
        }
        if (inner.Width < MinSide || inner.Height < MinSide) return;

        cell.IsGroup = true;
        cell.HasHeader = header;

        var total = node.Size;
        var scale = inner.Width * inner.Height / total;
        var kept = new List<ScanNode>();
        var sizes = new List<long>();
        long sum = 0;
        foreach (var child in node.Children)
        {
            if (child.Size <= 0 || child.Size * scale < MinSide * MinSide) break;
            kept.Add(child);
            sizes.Add(child.Size);
            sum += child.Size;
        }
        if (kept.Count == 0)
        {
            cell.IsGroup = false;
            cell.HasHeader = false;
            return;
        }
        if (total > sum) sizes.Add(total - sum);   // invisible lump of everything too small to draw

        var rects = Squarify(sizes, inner);
        for (var i = 0; i < kept.Count; i++)
        {
            var r = rects[i];
            if (r.Width < MinSide || r.Height < MinSide) continue;
            var child = new TreemapCell(kept[i], r, cell.Depth + 1);
            cells.Add(child);
            Recurse(child, maxDepth, cells);
        }
    }
}
