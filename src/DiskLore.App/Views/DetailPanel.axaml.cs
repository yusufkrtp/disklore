using Avalonia.Controls;
using DiskLore.App.Services;

namespace DiskLore.App.Views;

public partial class DetailPanel : UserControl
{
    public DetailPanel()
    {
        InitializeComponent();
    }

    public IAppContext? AppContext { get; set; }
}
