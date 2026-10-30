using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.App.Converters;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.ViewModels;

// One visible line of the tree grid: either a node or a "Show 500 more" row.
public sealed class TreeRowViewModel : ObservableObject
{
    public const double IndentPerLevel = 16;

    private readonly Func<ScanNode, Explanation>? _explain;
    private Explanation? _explanation;
    private bool _isExpanded;
    private int _depth;

    public TreeRowViewModel(ScanNode node, int depth, Func<ScanNode, Explanation>? explain)
    {
        Node = node;
        _depth = depth;
        _explain = explain;
    }

    private TreeRowViewModel(ScanNode parent, int depth, string moreText)
    {
        MoreParent = parent;
        IsShowMore = true;
        MoreText = moreText;
        _depth = depth;
    }

    public static TreeRowViewModel CreateShowMore(ScanNode parent, int depth, int remainingCount, long remainingBytes, int pageSize)
    {
        var next = Math.Min(pageSize, remainingCount);
        return new TreeRowViewModel(parent, depth,
            $"Show {next} more ({remainingCount:N0} items, {SizeFormat.Format(remainingBytes)})");
    }

    public ScanNode? Node { get; }
    public ScanNode? MoreParent { get; }
    public bool IsShowMore { get; }
    public string MoreText { get; } = "";

    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth == value) return;
            _depth = value;
            OnPropertyChanged(nameof(Depth));
            OnPropertyChanged(nameof(IndentWidth));
        }
    }

    public double IndentWidth => _depth * IndentPerLevel;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsNode => Node != null;
    public bool IsJunction => Node is { IsReparsePoint: true };
    public bool IsLocked => Node is { AccessDenied: true };
    public bool IsFolder => Node is { IsDirectory: true, IsReparsePoint: false, AccessDenied: false };
    public bool IsFile => Node is { IsDirectory: false };
    public bool IsLink => IsJunction;

    public bool HasChildren => Node is { IsDirectory: true, IsReparsePoint: false } && Node.Children.Count > 0;

    public string Name => Node?.Name ?? "";
    public string LinkText => IsJunction ? "→ " + (Node!.LinkTarget ?? "?") : "";
    public string SizeText => Node == null ? "" : SizeFormat.Format(Node.Size);

    public double Fraction
    {
        get
        {
            var n = Node;
            if (n == null) return 0;
            var parent = n.Parent;
            if (parent == null) return 1;
            return parent.Size <= 0 ? 0 : Math.Clamp((double)n.Size / parent.Size, 0, 1);
        }
    }

    public string PercentText
    {
        get
        {
            if (Node == null || IsJunction) return "";
            var f = Fraction * 100;
            return f >= 10 || f == 0 ? $"{f:0}%" : f >= 0.1 ? $"{f:0.0}%" : "<0.1%";
        }
    }

    public string ChangedText => Node == null || IsJunction ? "" : TimeFormat.Ago(Node.LastWriteUtc);

    public Explanation? Explanation
    {
        get
        {
            if (Node == null || _explain == null) return null;
            if (_explanation == null)
            {
                try { _explanation = _explain(Node); }
                catch (Exception) { _explanation = Fallback(Node); }
            }
            return _explanation;
        }
    }

    public Badge Badge => IsJunction || IsLocked ? Badge.Unknown : Explanation?.Badge ?? Badge.Unknown;
    public bool ShowBadge => Node != null && !IsJunction && !IsLocked;

    public string Summary
    {
        get
        {
            if (Node == null) return "";
            if (IsLocked) return "Not readable";
            if (IsJunction) return "";
            return Explanation?.Summary ?? "";
        }
    }

    // Called after an AI answer or other change; row text is recomputed on next read.
    public void InvalidateExplanation()
    {
        _explanation = null;
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(Summary));
    }

    // Called after sizes changed (delete).
    public void RefreshNumbers()
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(Fraction));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(ChangedText));
        OnPropertyChanged(nameof(HasChildren));
    }

    private static Explanation Fallback(ScanNode n)
        => new(n.Name, Badge.Unknown, null, "", "", "", "", null, null, ExplanationSource.None, 0, null);
}
