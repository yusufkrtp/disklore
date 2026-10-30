using Avalonia.Controls;
using Avalonia.Interactivity;
using DiskLore.App.ViewModels;

namespace DiskLore.App.Views;

// Shows the exact command before it runs. When the platform can't run elevated commands it only offers Copy.
public partial class ConfirmCommandDialog : Window
{
    private readonly string _command = "";

    public ConfirmCommandDialog()
    {
        InitializeComponent();
    }

    public ConfirmCommandDialog(string label, string command, bool canRun) : this()
    {
        _command = command;
        TitleText.Text = label;
        CommandText.Text = command;
        IntroText.Text = canRun
            ? "DiskLore will run this exact command:"
            : "This system can't run elevated commands from DiskLore. Copy it and run it yourself:";
        AdminText.IsVisible = canRun;
        RunButton.IsVisible = canRun;
        CopyButton.Classes.Set("primary", !canRun);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e) => await UiHelpers.CopyTextAsync(_command);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRun(object? sender, RoutedEventArgs e) => Close(true);
}
