using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace DiskLore.App.ViewModels;

public static class UiHelpers
{
    public static Window? MainWindow
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task CopyTextAsync(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(MainWindow)?.Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(text);
        }
        catch (Exception) { }
    }
}
