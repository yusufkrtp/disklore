using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.Controls;

public enum TreemapColorMode
{
    BySafety,
    ByFileType,
}

// Draws a squarified treemap of ZoomRoot. Layout is cached and recomputed only when the size, the zoom root
// or the tree changes; only the nodes that are actually drawn are laid out and explained.
public class TreemapControl : Control
{
    private static readonly Color[] Palette =
    [
        Color.Parse("#4C8DF6"), Color.Parse("#F59E42"), Color.Parse("#4CC38A"), Color.Parse("#E5604D"),
        Color.Parse("#A06CF0"), Color.Parse("#2BB5B0"), Color.Parse("#E6C229"), Color.Parse("#E873B2"),
        Color.Parse("#8FA33A"), Color.Parse("#5AC8E8"), Color.Parse("#B07A52"), Color.Parse("#8A94A8"),
    ];

    private static readonly Badge[] Badges = Enum.GetValues<Badge>();

    private ScanNode? _zoomRoot;
    private ScanNode? _selected;
    private TreemapColorMode _mode = TreemapColorMode.BySafety;
    private List<TreemapCell> _cells = [];
    private Dictionary<ScanNode, TreemapCell> _byNode = new();
    private Size _layoutSize;
    private bool _layoutDirty = true;
    private TreemapCell? _hover;
    private readonly Dictionary<(int Kind, uint Argb), Brush> _brushCache = new();
    private readonly Typeface _typeface = new(FontFamily.Default);

    public TreemapControl()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) =>
        {
            _brushCache.Clear();
            InvalidateVisual();
        };
    }

    public ScanNode? ZoomRoot
    {
        get => _zoomRoot;
        set
        {
            if (ReferenceEquals(_zoomRoot, value)) return;
            _zoomRoot = value;
            InvalidateLayout();
        }
    }

    public TreemapColorMode ColorMode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            InvalidateColors();
        }
    }

    // Supplies the explanation used for "By safety" colors and tooltips.
    public Func<ScanNode, Explanation>? Explainer { get; set; }

    public ScanNode? SelectedNode
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    public double LastLayoutMilliseconds { get; private set; }
    public int CellCount => _cells.Count;

    public event Action<ScanNode>? NodeClicked;
    public event Action<ScanNode>? NodeActivated;

    public void InvalidateLayout()
    {
        _layoutDirty = true;
        _hover = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    public void InvalidateColors()
    {
        foreach (var c in _cells)
        {
            c.BadgeIndex = -1;
            c.TypeIndex = -1;
        }
        InvalidateVisual();
    }

    // ---- layout ----

    private void EnsureLayout()
    {
        var size = Bounds.Size;
        if (!_layoutDirty && size == _layoutSize) return;
        _layoutDirty = false;
        _layoutSize = size;
        _hover = null;
        var sw = Stopwatch.StartNew();
        _cells = _zoomRoot == null ? [] : TreemapLayout.Build(_zoomRoot, new Rect(0, 0, size.Width, size.Height));
        _byNode = new Dictionary<ScanNode, TreemapCell>(_cells.Count);
        foreach (var c in _cells) _byNode[c.Node] = c;
        sw.Stop();
        LastLayoutMilliseconds = sw.Elapsed.TotalMilliseconds;
    }

    // ---- colors ----

    private Color ResourceColor(string key, Color fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush b) return b.Color;
        return fallback;
    }

    private IBrush ResourceBrush(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush b) return b;
        return Brushes.Gray;
    }

    private Brush Solid(int kind, Color color, double alpha)
    {
        var key = (kind, color.ToUInt32());
        if (_brushCache.TryGetValue(key, out var b)) return b;
        b = new SolidColorBrush(color, alpha);
        _brushCache[key] = b;
        return b;
    }

    private Color BaseColor(TreemapCell cell)
    {
        if (_mode == TreemapColorMode.BySafety)
        {
            if (cell.BadgeIndex < 0)
            {
                var badge = Explainer?.Invoke(cell.Node).Badge ?? Badge.Unknown;
                cell.BadgeIndex = Array.IndexOf(Badges, badge);
            }
            var b = Badges[cell.BadgeIndex];
            // Unknown is a neutral tone drawn from the theme itself: visibly different from the background,
            // never a loud gray slab.
            if (b == Badge.Unknown) return Mix(_bg, _fg, _dark ? 0.30 : 0.26);
            return ResourceColor("Badge." + b, Colors.Gray);
        }

        if (cell.TypeIndex < 0) cell.TypeIndex = TypeIndexOf(cell.Node);
        return Palette[cell.TypeIndex];
    }

    // Files hash their extension; folders follow their largest child down to a file (children are sorted by size).
    private static int TypeIndexOf(ScanNode node)
    {
        var n = node;
        while (n.IsDirectory && n.Children.Count > 0) n = n.Children[0];
        return StableHash(n.Extension) % Palette.Length;
    }

    internal static int StableHash(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var ch in s)
            {
                h ^= ch;
                h *= 16777619u;
            }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    private Color _bg = Colors.Black;
    private Color _fg = Colors.White;
    private bool _dark = true;

    internal static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    // Nested levels step towards the text color (lighter on dark themes, darker on light ones), so a folder reads
    // differently from the folder around it. Headers sit between the body tint and the leaf color.
    internal Color BodyTone(Color color, int depth)
        => Mix(Mix(_bg, color, _dark ? 0.17 : 0.14), _fg, 0.03 * Math.Min(depth - 1, 4));

    internal Color HeaderTone(Color color, int depth)
        => Mix(Mix(_bg, color, _dark ? 0.50 : 0.58), _fg, 0.035 * Math.Min(depth - 1, 4));

    internal Color LeafTone(Color color, int depth, string name)
    {
        // Deeper leaves fade a little into the background; a name-derived nudge separates equal-colored siblings.
        var tone = Mix(color, _bg, 0.08 + 0.06 * Math.Min(depth - 1, 4));
        var nudge = (StableHash(name) % 5 - 2) * 0.035;
        return nudge >= 0 ? Mix(tone, _fg, nudge) : Mix(tone, _bg, -nudge);
    }

    private static Color TextOn(Color c)
    {
        var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return lum > 0.55 ? Color.FromRgb(20, 22, 26) : Color.FromRgb(250, 250, 250);
    }

    // ---- drawing ----

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var full = new Rect(Bounds.Size);
        context.FillRectangle(ResourceBrush("App.Surface"), full);
        if (full.Width < 1 || full.Height < 1) return;

        EnsureLayout();
        _bg = ResourceColor("App.Background", Colors.Black);
        _fg = ResourceColor("App.Text", Colors.White);
        _dark = Luminance(_bg) < 0.5;
        var gap = new Pen(ResourceBrush("App.Background"), 1);

        foreach (var cell in _cells)
        {
            if (cell.Depth == 0) continue;
            var color = BaseColor(cell);
            var r = cell.Rect;
            if (cell.IsGroup)
            {
                context.DrawRectangle(Solid(0, BodyTone(color, cell.Depth), 1), gap, r);
                if (cell.HasHeader)
                {
                    var hr = cell.HeaderRect;
                    var header = HeaderTone(color, cell.Depth);
                    context.FillRectangle(Solid(1, header, 1), hr);
                    DrawLabel(context, cell, hr, TextOn(header), true);
                }
            }
            else
            {
                var leaf = LeafTone(color, cell.Depth, cell.Node.Name);
                context.DrawRectangle(Solid(2, leaf, 1), gap, r);
                if (r.Width >= 48 && r.Height >= 16) DrawLabel(context, cell, r, TextOn(leaf), false);
            }
        }

        if (_selected != null && _byNode.TryGetValue(_selected, out var sel) && sel.Depth > 0)
            context.DrawRectangle(null, new Pen(ResourceBrush("App.Text"), 2), sel.Rect.Deflate(1));
        if (_hover != null && _hover.Depth > 0)
            context.DrawRectangle(null, new Pen(ResourceBrush("App.Accent"), 2), _hover.Rect.Deflate(1));
    }

    private void DrawLabel(DrawingContext context, TreemapCell cell, Rect area, Color textColor, bool header)
    {
        if (!cell.LabelBuilt || cell.LabelColor != textColor)
        {
            cell.LabelBuilt = true;
            cell.LabelColor = textColor;
            var text = cell.Node.Name;
            if (!header && area.Height >= 32) text += "\n" + SizeFormat.Format(cell.Node.Size);
            cell.Label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, 11,
                new SolidColorBrush(textColor))
            {
                MaxTextWidth = Math.Max(1, area.Width - 6),
                MaxTextHeight = Math.Max(1, header ? 40 : area.Height - 2),
                MaxLineCount = header ? 1 : int.MaxValue,
                Trimming = TextTrimming.CharacterEllipsis,
            };
        }
        if (cell.Label == null) return;
        using (context.PushClip(area))
            context.DrawText(cell.Label, new Point(area.X + 3, area.Y + (header ? 0.5 : 2)));
    }

    // ---- input ----

    private TreemapCell? HitTest(Point p)
    {
        for (var i = _cells.Count - 1; i >= 1; i--)
            if (_cells[i].Rect.Contains(p)) return _cells[i];
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (ReferenceEquals(hit, _hover)) return;
        _hover = hit;
        ToolTip.SetTip(this, hit == null ? null : TooltipFor(hit.Node));
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover == null) return;
        _hover = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var hit = HitTest(e.GetPosition(this));
        if (hit == null) return;
        if (e.ClickCount >= 2) NodeActivated?.Invoke(hit.Node);
        else NodeClicked?.Invoke(hit.Node);
        e.Handled = true;
    }

    private string TooltipFor(ScanNode node)
    {
        var share = _zoomRoot is { Size: > 0 } ? 100.0 * node.Size / _zoomRoot.Size : 0;
        var text = $"{node.Name}\n{SizeFormat.Format(node.Size)}  ·  {share:0.#}% of {_zoomRoot?.Name}";
        if (Explainer != null)
        {
            var e = Explainer(node);
            text += $"\n{BadgeLabel(e.Badge)}" + (string.IsNullOrEmpty(e.Summary) ? "" : " — " + e.Summary);
        }
        return text;
    }

    private static string BadgeLabel(Badge b) => BadgePill.LabelFor(b);
}
