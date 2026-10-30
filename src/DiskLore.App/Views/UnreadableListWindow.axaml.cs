using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiskLore.App.Views;

public partial class UnreadableListWindow : Window
{
    public UnreadableListWindow()
    {
        InitializeComponent();
    }

    public UnreadableListWindow(IReadOnlyList<string> paths) : this()
    {
        List.ItemsSource = paths;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
