using DiskLore.Core.Explanations;

namespace DiskLore.App.Converters;

public static class BadgeInfo
{
    public static string ShortText(Badge b) => b switch
    {
        Badge.Critical => "CRITICAL",
        Badge.Shrink => "SHRINK",
        Badge.App => "APP",
        Badge.Check => "CHECK",
        Badge.Safe => "SAFE",
        Badge.Leftover => "LEFTOVER",
        Badge.User => "USER",
        _ => "?"
    };
}

public static class TimeFormat
{
    // "just now", "5 minutes ago", "3 days ago", "2 years ago".
    public static string Ago(DateTime utc, DateTime? nowUtc = null)
    {
        if (utc == default || utc.Year < 1980) return "";
        var span = (nowUtc ?? DateTime.UtcNow) - utc;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalHours < 24) return Plural((int)span.TotalHours, "hour");
        if (span.TotalDays < 30) return Plural((int)span.TotalDays, "day");
        if (span.TotalDays < 365) return Plural((int)(span.TotalDays / 30), "month");
        return Plural((int)(span.TotalDays / 365), "year");
    }

    public static string Date(DateTime utc)
        => utc == default || utc.Year < 1980 ? "" : utc.ToLocalTime().ToString("d MMM yyyy");

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
}

public static class TextTrim
{
    // "C:\Users\Alice\...\deep\file.bin" style middle ellipsis.
    public static string Middle(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max || max < 8) return text;
        int keepEnd = (max - 1) * 2 / 3;
        int keepStart = max - 1 - keepEnd;
        return text[..keepStart] + "…" + text[^keepEnd..];
    }
}
