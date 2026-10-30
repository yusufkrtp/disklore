using Avalonia.Controls;
using DiskLore.App.Services;

namespace DiskLore.App.Views.Tabs;

public partial class ChangesView : UserControl
{
    public ChangesView()
    {
        InitializeComponent();
    }

    public IAppContext? AppContext { get; set; }
}
