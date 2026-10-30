using Avalonia.Controls;
using Avalonia.Input;
using DiskLore.App.Services;
using DiskLore.App.ViewModels.Tabs;

namespace DiskLore.App.Views.Tabs;

public partial class ChangesView : UserControl
{
    private readonly ChangesViewModel _vm = new();
    private readonly DeferredRefresh _refresh;
    private IAppContext? _ctx;

    public ChangesView()
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
            if (_ctx != null) _ctx.CurrentChanged -= OnCurrentChanged;
            _ctx = value;
            if (_ctx != null) _ctx.CurrentChanged += OnCurrentChanged;
            _refresh.Mark();
        }
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => _refresh.Mark();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => NavigateToSelected();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NavigateToSelected();
        e.Handled = true;
    }

    private void NavigateToSelected()
    {
        if ((List.SelectedItem as ChangeRow)?.Node is { } node) _ctx?.Navigate(node);
    }
}
