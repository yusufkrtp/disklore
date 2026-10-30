using Avalonia.Controls;
using DiskLore.App.Services;

namespace DiskLore.App.Views.Tabs;

public partial class LargestFilesView : UserControl
{
    public LargestFilesView()
    {
        InitializeComponent();
    }

    public IAppContext? AppContext { get; set; }
}
