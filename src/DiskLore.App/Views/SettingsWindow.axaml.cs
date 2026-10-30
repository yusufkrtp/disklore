using Avalonia.Controls;
using Avalonia.Interactivity;
using DiskLore.App.ViewModels;

namespace DiskLore.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(MainViewModel main) : this()
    {
        DataContext = new SettingsViewModel(main);
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();
}
