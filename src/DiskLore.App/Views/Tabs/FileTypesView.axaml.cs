using Avalonia.Controls;
using DiskLore.App.Services;

namespace DiskLore.App.Views.Tabs;

public partial class FileTypesView : UserControl
{
    public FileTypesView()
    {
        InitializeComponent();
    }

    public IAppContext? AppContext { get; set; }
}
