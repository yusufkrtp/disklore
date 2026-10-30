using Avalonia.Controls;
using DiskLore.App.Services;
using DiskLore.App.ViewModels.Tabs;

namespace DiskLore.App.Views.Tabs;

public partial class FileTypesView : UserControl
{
    private readonly FileTypesViewModel _vm = new();
    private readonly DeferredRefresh _refresh;
    private IAppContext? _ctx;

    public FileTypesView()
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
                _ctx.CurrentChanged -= OnChanged;
                _ctx.SelectedChanged -= OnChanged;
            }
            _ctx = value;
            if (_ctx != null)
            {
                _ctx.CurrentChanged += OnChanged;
                _ctx.SelectedChanged += OnChanged;
            }
            _refresh.Mark();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => _refresh.Mark();
}
