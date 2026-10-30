using Avalonia.Controls;
using DiskLore.App.ViewModels;

namespace DiskLore.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;
        TreeTab.AppContext = vm;
        Detail.AppContext = vm;
        LargestFilesTab.AppContext = vm;
        FileTypesTab.AppContext = vm;
        TreemapTab.AppContext = vm;
        ChangesTab.AppContext = vm;
    }
}
