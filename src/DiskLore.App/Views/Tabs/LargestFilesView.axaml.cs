using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiskLore.App.Services;
using DiskLore.App.ViewModels.Tabs;
using DiskLore.Core.Model;

namespace DiskLore.App.Views.Tabs;

public partial class LargestFilesView : UserControl
{
    private readonly LargestFilesViewModel _vm = new();
    private readonly DeferredRefresh _refresh;
    private IAppContext? _ctx;

    public LargestFilesView()
    {
        InitializeComponent();
        DataContext = _vm;
        _refresh = new DeferredRefresh(this, () => _ = _vm.LoadAsync(_ctx));
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
                _ctx.Explanations.ExplanationChanged -= OnExplanationChanged;
            }
            _ctx = value;
            if (_ctx != null)
            {
                _ctx.CurrentChanged += OnCurrentChanged;
                _ctx.Explanations.ExplanationChanged += OnExplanationChanged;
            }
            _refresh.Mark();
        }
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => _refresh.Mark();

    private void OnExplanationChanged(ScanNode node)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => _vm.RefreshExplanations(_ctx));

    private ScanNode? SelectedNode => (List.SelectedItem as LargestFileRow)?.Node;

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => NavigateToSelected();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NavigateToSelected();
        e.Handled = true;
    }

    private void NavigateToSelected()
    {
        if (SelectedNode is { } node) _ctx?.Navigate(node);
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (SelectedNode is { } node) _ctx?.Platform.RevealInFileManager(node.FullPath);
    }

    private void OnRecycle(object? sender, RoutedEventArgs e) => _ = DeleteSelected(false);

    private void OnDeletePermanently(object? sender, RoutedEventArgs e) => _ = DeleteSelected(true);

    private async Task DeleteSelected(bool permanent)
    {
        if (SelectedNode is { } node && _ctx != null) await _ctx.DeleteAsync(node, permanent);
    }
}
