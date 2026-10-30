using Avalonia;
using DiskLore.App.Controls;
using DiskLore.Core.Model;
using Xunit;

namespace DiskLore.Tests.Treemap;

public class TreemapLayoutTests
{
    private static readonly Rect Bounds = new(10, 20, 800, 500);

    [Fact]
    public void Areas_are_proportional_inside_bounds_without_overlap()
    {
        long[] sizes = [600, 300, 200, 150, 100, 80, 60, 40, 30, 20, 10, 5];
        var rects = TreemapLayout.Squarify(sizes, Bounds);
        var total = sizes.Sum();
        var boundsArea = Bounds.Width * Bounds.Height;

        for (var i = 0; i < sizes.Length; i++)
        {
            var expected = boundsArea * sizes[i] / total;
            var actual = rects[i].Width * rects[i].Height;
            Assert.InRange(actual / expected, 0.99, 1.01);
            Assert.True(rects[i].X >= Bounds.X - 1e-6 && rects[i].Y >= Bounds.Y - 1e-6);
            Assert.True(rects[i].Right <= Bounds.Right + 1e-6 && rects[i].Bottom <= Bounds.Bottom + 1e-6);
        }

        for (var i = 0; i < rects.Length; i++)
            for (var j = i + 1; j < rects.Length; j++)
            {
                var overlap = rects[i].Intersect(rects[j]);
                Assert.True(overlap.Width * overlap.Height < 1e-6, $"{i} overlaps {j}");
            }

        Assert.InRange(rects.Sum(r => r.Width * r.Height) / boundsArea, 0.999, 1.001);
    }

    [Fact]
    public void Aspect_ratios_stay_reasonable()
    {
        var sizes = Enumerable.Range(1, 40).Select(i => (long)(1000 / i + 10)).ToArray();
        var rects = TreemapLayout.Squarify(sizes, new Rect(0, 0, 1000, 600));
        var worst = rects.Max(r => Math.Max(r.Width / r.Height, r.Height / r.Width));
        Assert.True(worst < 8, $"worst aspect {worst}");
    }

    [Fact]
    public void Degenerate_input_does_not_throw()
    {
        Assert.Empty(TreemapLayout.Squarify([], Bounds));
        Assert.Single(TreemapLayout.Squarify([0], Bounds));
        var single = TreemapLayout.Squarify([5], Bounds)[0];
        Assert.Equal(Bounds.Width, single.Width, 3);
        Assert.Equal(Bounds.Height, single.Height, 3);
    }

    private static ScanNode Add(ScanNode parent, string name, long size, bool dir)
    {
        var n = new ScanNode(name, parent, dir) { Size = size };
        parent.AddChild(n);
        return n;
    }

    [Fact]
    public void Build_culls_tiny_items_and_keeps_cells_inside_bounds()
    {
        var root = new ScanNode(Path.GetFullPath("r"), null, true);
        long total = 0;
        var big = Add(root, "big", 900_000, true);
        Add(big, "a.bin", 500_000, false);
        Add(big, "b.bin", 400_000, false);
        total += 900_000;
        // 200 tiny files: each is far below 3x3 px in a 400x300 map.
        for (var i = 0; i < 200; i++) { Add(root, $"tiny{i}", 10, false); total += 10; }
        root.Size = total;
        root.SortChildrenBySizeDescending();

        var cells = TreemapLayout.Build(root, new Rect(0, 0, 400, 300));
        Assert.Equal(root, cells[0].Node);
        Assert.DoesNotContain(cells, c => c.Node.Name.StartsWith("tiny"));
        Assert.Contains(cells, c => c.Node.Name == "a.bin");
        foreach (var c in cells)
        {
            Assert.True(c.Rect.Width >= TreemapLayout.MinSide || c.Depth == 0);
            Assert.True(c.Rect.X >= -1e-6 && c.Rect.Right <= 400 + 1e-6 && c.Rect.Bottom <= 300 + 1e-6);
        }
    }

    [Fact]
    public void Build_limits_depth_and_adds_header_to_big_folders()
    {
        var root = new ScanNode(Path.GetFullPath("r"), null, true);
        var d1 = Add(root, "d1", 1000, true);
        var d2 = Add(d1, "d2", 1000, true);
        var d3 = Add(d2, "d3", 1000, true);
        var d4 = Add(d3, "d4", 1000, true);
        Add(d4, "deep.bin", 1000, false);
        root.Size = 1000;

        var cells = TreemapLayout.Build(root, new Rect(0, 0, 600, 400));
        Assert.Equal(4, cells.Count);                 // zoom root + 3 levels
        Assert.True(cells[1].HasHeader);
        Assert.DoesNotContain(cells, c => c.Node.Name == "d4");
        Assert.All(cells.Where(c => c.Depth > 0), c => Assert.True(c.Depth <= 3));
    }

    [Fact]
    public void Layout_of_a_wide_folder_is_fast()
    {
        var root = new ScanNode(Path.GetFullPath("r"), null, true);
        long total = 0;
        for (var i = 0; i < 100_000; i++)
        {
            var size = 1_000_000L / (i + 1) + 1;
            Add(root, "f" + i, size, false);
            total += size;
        }
        root.Size = total;
        root.SortChildrenBySizeDescending();

        TreemapLayout.Build(root, new Rect(0, 0, 1200, 800));   // warm up
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cells = TreemapLayout.Build(root, new Rect(0, 0, 1200, 800));
        sw.Stop();
        Assert.True(cells.Count < 20_000);
        // 50 ms is the interactive target; parallel test runs and CI runners get headroom.
        var limitMs = Environment.GetEnvironmentVariable("CI") is null ? 150 : 500;
        Assert.True(sw.ElapsedMilliseconds < limitMs, $"layout took {sw.ElapsedMilliseconds} ms");
    }
}
