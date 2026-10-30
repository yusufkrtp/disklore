using System.Globalization;

namespace DiskLore.App.ViewModels.Tabs;

// Small text helpers shared by the tab view models.
public static class TabText
{
    // "C:\Users\me\...\very\long\folder" -> keeps the start and the end, drops the middle.
    public static string MiddleTrim(string text, int max)
    {
        if (max < 8 || text.Length <= max) return text;
        var keepEnd = (max - 1) * 2 / 3;
        var keepStart = max - 1 - keepEnd;
        return text[..keepStart] + "…" + text[^keepEnd..];
    }

    public static string Date(DateTime utc)
    {
        if (utc == default) return "";
        var local = utc.Kind == DateTimeKind.Local ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public static string DateAndTime(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Local ? utc : System.DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
    }
}
