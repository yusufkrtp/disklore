using Avalonia.Controls;
using DiskLore.App.Services;

namespace DiskLore.App.Views.Tabs;

public partial class TreemapView : UserControl
{
    public TreemapView()
    {
        InitializeComponent();
    }

    public IAppContext? AppContext { get; set; }
}
