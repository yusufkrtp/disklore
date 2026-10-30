using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DiskLore.App.Themes;
using DiskLore.App.Views;

namespace DiskLore.App;

public partial class App : Application
{
    // Optional path from the command line; consumed by the main view model on startup.
    public static string? StartupPath { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Provisional theme so the first frame is never unstyled; the saved choice is applied by MainViewModel.
        ThemeManager.Apply(ThemeManager.System);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
