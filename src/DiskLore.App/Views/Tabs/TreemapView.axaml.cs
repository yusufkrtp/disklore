using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DiskLore.App.Controls;
using DiskLore.App.Services;
using DiskLore.Core.Model;

namespace DiskLore.App.Views.Tabs;

public partial class TreemapView : UserControl
{
    private IAppContext? _ctx;

    public TreemapView()
    {
        InitializeComponent();
        Map.NodeClicked += node =>
        {
            if (_ctx != null) _ctx.Selected = node;
        };
        Map.NodeActivated += node =>
        {
            if (node.IsDirectory) ZoomTo(node);
            else if (_ctx != null) _ctx.Selected = node;
        };
        RebuildCrumbs();
    }

    public IAppContext? AppContext
    {
        get => _ctx;
        set
        {
            if (ReferenceEquals(_ctx, value)) return;
            if (_ctx != null)
            {
                _ctx.CurrentChanged -= OnCurrentChanged;
                _ctx.SelectedChanged -= OnSelectedChanged;
                _ctx.Explanations.ExplanationChanged -= OnExplanationChanged;
            }
            _ctx = value;
            if (_ctx != null)
            {
                _ctx.CurrentChanged += OnCurrentChanged;
                _ctx.SelectedChanged += OnSelectedChanged;
                _ctx.Explanations.ExplanationChanged += OnExplanationChanged;
                var explanations = _ctx.Explanations;
                Map.Explainer = explanations.Explain;
            }
            else
            {
                Map.Explainer = null;
            }
            Map.ZoomRoot = null;
            Reload();
            OnSelectedChanged(null, EventArgs.Empty);
        }
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reload);

    private void OnSelectedChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => Map.SelectedNode = _ctx?.Selected);

    private void OnExplanationChanged(ScanNode node) => Dispatcher.UIThread.Post(Map.InvalidateColors);

    // New scan or a changed tree: keep the zoom if that folder is still part of the tree, else go back to the root.
    private void Reload()
    {
        var root = _ctx?.Current?.Root;
        EmptyText.IsVisible = root == null;
        if (root == null)
        {
            Map.ZoomRoot = null;
        }
        else if (Map.ZoomRoot == null || !IsInTree(Map.ZoomRoot, root))
        {
            Map.ZoomRoot = root;
        }
        else
        {
            Map.InvalidateLayout();
        }
        RebuildCrumbs();
    }

    private static bool IsInTree(ScanNode node, ScanNode root)
    {
        for (var n = node; n != null; n = n.Parent)
            if (ReferenceEquals(n, root)) return true;
        return false;
    }

    private void ZoomTo(ScanNode node)
    {
        Map.ZoomRoot = node;
        RebuildCrumbs();
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (BySafety == null || ByType == null) return;
        Map.ColorMode = ByType.IsChecked == true ? TreemapColorMode.ByFileType : TreemapColorMode.BySafety;
    }

    private void RebuildCrumbs()
    {
        Crumbs.Children.Clear();
        var zoom = Map.ZoomRoot;
        if (zoom == null) return;

        var chain = new List<ScanNode>();
        for (var n = zoom; n != null; n = n.Parent) chain.Add(n);
        chain.Reverse();

        // Stop at the scan root: a zoom root outside the current scan has nothing above it we can show.
        var scanRoot = _ctx?.Current?.Root;
        if (scanRoot != null)
        {
            var i = chain.FindIndex(n => ReferenceEquals(n, scanRoot));
            if (i > 0) chain = chain.Skip(i).ToList();
        }

        var up = new Button { Content = "↑ Up", IsEnabled = chain.Count > 1 };
        up.Classes.Add("crumb");
        up.Click += (_, _) =>
        {
            if (chain.Count > 1) ZoomTo(chain[^2]);
        };
        Crumbs.Children.Add(up);

        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (i > 0) Crumbs.Children.Add(Muted("›"));
            if (i == chain.Count - 1)
            {
                var current = new TextBlock { Text = node.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(6, 0) };
                current.Bind(TextBlock.ForegroundProperty, current.GetResourceObservable("App.Text"));
                Crumbs.Children.Add(current);
            }
            else
            {
                var b = new Button { Content = node.Name };
                b.Classes.Add("crumb");
                b.Click += (_, _) => ZoomTo(node);
                Crumbs.Children.Add(b);
            }
        }
    }

    private TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("App.TextMuted"));
        return t;
    }
}
