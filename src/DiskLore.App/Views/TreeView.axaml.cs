using Avalonia.Controls;
using DiskLore.App.Services;

namespace DiskLore.App.Views;

public partial class DiskTreeView : UserControl
{
    public DiskTreeView()
    {
        InitializeComponent();
    }

    public IAppContext? AppContext { get; set; }
}
