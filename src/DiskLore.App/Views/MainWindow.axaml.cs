using Avalonia.Controls;
using Avalonia.Input;
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

        Opened += (_, _) =>
        {
            vm.Start();
#if DEBUG
            if (DebugShots.Active) DebugShots.Run(this, vm);
#endif
        };
    }

    public MainViewModel ViewModel => (MainViewModel)DataContext!;

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.Enter)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.ClearSearch();
            e.Handled = true;
        }
    }
}
