using Avalonia;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;

namespace DiskLore.App.Themes;

public sealed record ThemeOption(string Id, string Name, IBrush Background, IBrush Surface, IBrush Accent, IBrush Text);

public static class ThemeManager
{
    public const string System = "system";

    public static readonly string[] ThemeIds =
        { "Light", "Dark", "Midnight", "Forest", "Ember", "Rose", "Ocean", "HighContrast" };

    private static readonly string[] LightIds = { "Light", "Rose" };

    private static ResourceInclude? _current;
    private static bool _followHooked;
    private static bool _followSystem;

    public static string DisplayName(string id) => id switch
    {
        System => "Follow system",
        "HighContrast" => "High contrast",
        _ => id
    };

    // Choices in Settings order, each with swatch colors read from the theme dictionary.
    public static IReadOnlyList<ThemeOption> Options()
    {
        var list = new List<ThemeOption>();
        foreach (var id in new[] { System, "Light", "Dark", "Midnight", "Forest", "Ember", "Rose", "Ocean", "HighContrast" })
            list.Add(MakeOption(id));
        return list;
    }

    private static ThemeOption MakeOption(string id)
    {
        var dictId = id == System ? (SystemIsLight() ? "Light" : "Dark") : id;
        var dict = new ResourceInclude(new Uri("avares://DiskLore/")) { Source = new Uri($"avares://DiskLore/Themes/{dictId}.axaml") };
        IBrush Get(string key) => dict.Loaded.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Gray;
        return new ThemeOption(id, DisplayName(id), Get("App.Background"), Get("App.Surface"), Get("App.Accent"), Get("App.Text"));
    }

    public static bool SystemIsLight()
    {
        try
        {
            var settings = Application.Current?.PlatformSettings;
            if (settings != null)
                return settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Light;
        }
        catch { }
        return false;
    }

    // Swaps the merged theme dictionary and sets the matching theme variant. "system" follows the OS live.
    public static void Apply(string themeId)
    {
        var app = Application.Current;
        if (app == null) return;

        _followSystem = string.Equals(themeId, System, StringComparison.OrdinalIgnoreCase);
        if (_followSystem)
        {
            HookSystemChanges(app);
            themeId = SystemIsLight() ? "Light" : "Dark";
        }
        else
        {
            themeId = ThemeIds.FirstOrDefault(i => string.Equals(i, themeId, StringComparison.OrdinalIgnoreCase)) ?? "Dark";
        }
        ApplyConcrete(app, themeId);
    }

    private static void HookSystemChanges(Application app)
    {
        if (_followHooked || app.PlatformSettings == null) return;
        _followHooked = true;
        app.PlatformSettings.ColorValuesChanged += (_, _) =>
        {
            if (!_followSystem) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyConcrete(app, SystemIsLight() ? "Light" : "Dark"));
        };
    }

    private static void ApplyConcrete(Application app, string themeId)
    {
        var merged = app.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        _current = new ResourceInclude(new Uri("avares://DiskLore/"))
        {
            Source = new Uri($"avares://DiskLore/Themes/{themeId}.axaml")
        };
        merged.Add(_current);

        app.RequestedThemeVariant = Array.IndexOf(LightIds, themeId) >= 0 ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
