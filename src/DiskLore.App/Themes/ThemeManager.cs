using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace DiskLore.App.Themes;

public static class ThemeManager
{
    public static readonly string[] ThemeIds =
        { "Light", "Dark", "Midnight", "Forest", "Ember", "Rose", "Ocean", "HighContrast" };

    private static ResourceInclude? _current;

    // Swaps the merged theme dictionary and sets the matching theme variant.
    public static void Apply(string themeId)
    {
        var app = Application.Current;
        if (app == null) return;
        if (Array.IndexOf(ThemeIds, themeId) < 0) themeId = "Dark";

        var merged = app.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        _current = new ResourceInclude(new Uri("avares://DiskLore/"))
        {
            Source = new Uri($"avares://DiskLore/Themes/{themeId}.axaml")
        };
        merged.Add(_current);

        app.RequestedThemeVariant = themeId == "Light" || themeId == "Rose"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}
