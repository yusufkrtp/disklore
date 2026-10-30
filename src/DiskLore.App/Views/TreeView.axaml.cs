using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiskLore.App.Services;
using DiskLore.App.ViewModels;
using DiskLore.Core.Model;

namespace DiskLore.App.Views;

public partial class DiskTreeView : UserControl
{
    private bool _syncing;

    public DiskTreeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
    }

    public IAppContext? AppContext { get; set; }

    private MainViewModel? Vm => DataContext as MainViewModel;
    private MainViewModel? _hooked;

    private void Hook()
    {
        if (_hooked != null)
        {
            _hooked.SelectedChanged -= OnVmSelectedChanged;
            _hooked.RevealRequested -= OnRevealRequested;
        }
        _hooked = Vm;
        if (_hooked == null) return;
        _hooked.SelectedChanged += OnVmSelectedChanged;
        _hooked.RevealRequested += OnRevealRequested;
    }

    // Selection that came from elsewhere (treemap, largest files) is mirrored into the list.
    private void OnVmSelectedChanged(object? sender, EventArgs e)
    {
        var vm = Vm;
        if (vm?.Selected == null || _syncing) return;
        var row = vm.Tree.FindRow(vm.Selected);
        if (row != null && !ReferenceEquals(Rows.SelectedItem, row))
        {
            _syncing = true;
            Rows.SelectedItem = row;
            _syncing = false;
        }
    }

    private void OnRevealRequested(ScanNode node)
    {
        var vm = Vm;
        if (vm == null) return;
        var row = vm.Tree.FindRow(node) ?? vm.Tree.ExpandTo(node);
        if (row == null) return;
        _syncing = true;
        Rows.SelectedItem = row;
        _syncing = false;
        // The tab may have just become visible; scroll after layout.
        Dispatcher.UIThread.Post(() =>
        {
            Rows.ScrollIntoView(row);
            Rows.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (Rows.SelectedItem is TreeRowViewModel { Node: { } node } && Vm != null)
        {
            _syncing = true;
            Vm.Selected = node;
            _syncing = false;
        }
    }

    // ---- sorting -----------------------------------------------------------------------------

    private void OnSortName(object? sender, RoutedEventArgs e) => Sort(TreeSort.Name);
    private void OnSortSize(object? sender, RoutedEventArgs e) => Sort(TreeSort.Size);
    private void OnSortChanged(object? sender, RoutedEventArgs e) => Sort(TreeSort.Changed);

    private void Sort(TreeSort column)
    {
        Vm?.Tree.SetSort(column);
        if (Vm?.Selected is { } sel && Vm.Tree.FindRow(sel) is { } row)
        {
            _syncing = true;
            Rows.SelectedItem = row;
            _syncing = false;
        }
    }

    // ---- row interaction ---------------------------------------------------------------------

    private static TreeRowViewModel? RowOf(object? sender)
        => (sender as Avalonia.StyledElement)?.DataContext as TreeRowViewModel;

    private void OnChevronClick(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Vm?.Tree.Toggle(row);
        e.Handled = true;
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowOf(sender) is { IsShowMore: false } row) Vm?.Tree.Toggle(row);
    }

    private void OnShowMoreClick(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { IsShowMore: true } row)
        {
            var first = Vm?.Tree.ShowMore(row);
            if (first != null) Rows.SelectedItem = first;
        }
        e.Handled = true;
    }

    private async void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || Rows.SelectedItem is not TreeRowViewModel row) return;
        var tree = vm.Tree;

        switch (e.Key)
        {
            case Key.Right:
                if (row.IsShowMore) break;
                if (row.HasChildren && !row.IsExpanded) tree.Expand(row);
                else if (row.IsExpanded)
                {
                    var i = tree.Rows.IndexOf(row);
                    if (i >= 0 && i + 1 < tree.Rows.Count) Rows.SelectedItem = tree.Rows[i + 1];
                }
                e.Handled = true;
                break;

            case Key.Left:
                if (row.IsExpanded) tree.Collapse(row);
                else if (tree.ParentRow(row) is { } parent) Rows.SelectedItem = parent;
                e.Handled = true;
                break;

            case Key.Enter:
                if (row.IsShowMore)
                {
                    var first = tree.ShowMore(row);
                    if (first != null) Rows.SelectedItem = first;
                }
                else tree.Toggle(row);
                e.Handled = true;
                break;

            case Key.Delete:
                if (row.Node is { } node)
                {
                    e.Handled = true;
                    var permanent = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    await vm.DeleteAsync(node, permanent);
                }
                break;
        }
    }

    // ---- context menu ------------------------------------------------------------------------

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.DataContext is not TreeRowViewModel row || row.Node == null)
        {
            e.Cancel = true;
            return;
        }
        var vm = Vm;
        if (vm == null) { e.Cancel = true; return; }

        // Right-click selects the row first, like a file manager.
        Rows.SelectedItem = row;

        MenuItem Find(string name) => menu.Items.OfType<MenuItem>().First(m => m.Name == name);
        var node = row.Node;
        Find("MiAsk").IsVisible = !node.IsReparsePoint;
        Find("MiAsk").Header = row.Explanation?.Source == DiskLore.Core.Explanations.ExplanationSource.AI ? "Re-ask AI" : "Ask AI";
        Find("MiLock").IsVisible = vm.Platform.SupportsLockDetection;
        Find("MiRecycle").Header = OperatingSystem.IsWindows() ? "Move to Recycle Bin" : "Move to Trash";
        var deletable = node.Parent != null;
        Find("MiRecycle").IsEnabled = deletable;
        Find("MiDelete").IsEnabled = deletable;
    }

    private static TreeRowViewModel? MenuRow(object? sender)
        => (sender as MenuItem)?.DataContext as TreeRowViewModel;

    private void OnMenuOpen(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n && Vm != null)
        {
            try { Vm.Platform.RevealInFileManager(n.FullPath); }
            catch (Exception ex) { Vm.StatusText = ex.Message; }
        }
    }

    private async void OnMenuCopy(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n) await UiHelpers.CopyTextAsync(n.FullPath);
    }

    private async void OnMenuAsk(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n && Vm != null) await Vm.AskAiAsync(n);
    }

    private void OnMenuLock(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n && Vm != null)
        {
            Vm.Selected = n;
            _ = Vm.Detail.CheckInUse();
        }
    }

    private void OnMenuTreemap(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n) Vm?.ShowInTreemap(n);
    }

    private async void OnMenuRecycle(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n && Vm != null) await Vm.DeleteAsync(n, false);
    }

    private async void OnMenuDelete(object? sender, RoutedEventArgs e)
    {
        if (MenuRow(sender)?.Node is { } n && Vm != null) await Vm.DeleteAsync(n, true);
    }

    // ---- search results ----------------------------------------------------------------------

    private void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SearchResultViewModel r } list)
        {
            list.SelectedItem = null;
            Vm?.OpenSearchResult(r);
        }
    }
}
