using Avalonia;

namespace DiskLore.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        args = DebugShots.Parse(args);
#endif
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            App.StartupPath = args[0];

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
