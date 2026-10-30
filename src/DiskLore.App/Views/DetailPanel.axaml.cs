using Avalonia.Controls;
using DiskLore.App.Services;
using DiskLore.App.ViewModels;

namespace DiskLore.App.Views;

public partial class DetailPanel : UserControl
{
    public DetailPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { };
    }

    private IAppContext? _appContext;

    public IAppContext? AppContext
    {
        get => _appContext;
        set
        {
            _appContext = value;
            // The panel binds to the main view model's DetailViewModel.
            DataContext = (value as MainViewModel)?.Detail;
            (value as MainViewModel)?.Detail.Refresh();
        }
    }
}
