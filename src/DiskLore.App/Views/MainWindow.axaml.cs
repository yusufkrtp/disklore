using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        // Tunnel so the tree's type-ahead and the text boxes don't swallow the shortcuts first.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            vm.Start();
#if DEBUG
            if (DebugShots.Active) DebugShots.Run(this, vm);
#endif
        };
    }

    public MainViewModel ViewModel => (MainViewModel)DataContext!;

    // Ctrl+F focuses search, F5 rescans, Esc closes the search results.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            if (!vm.HasResult) return;
            vm.ShowStart = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            });
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            if (vm.RescanCommand.CanExecute(null)) vm.RescanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.IsSearchOpen)
        {
            vm.ClearSearch();
            e.Handled = true;
        }
    }

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
