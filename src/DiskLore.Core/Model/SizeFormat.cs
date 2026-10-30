using System.Globalization;

namespace DiskLore.Core.Model;

public static class SizeFormat
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    // 1024-based, one decimal from KB up: "512 B", "980 MB", "12.4 GB".
    public static string Format(long bytes)
    {
        if (bytes < 0) return "-" + Format(-bytes);
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    // Signed delta for diffs: "+18.4 GB", "-300 MB".
    public static string FormatDelta(long bytes) => bytes >= 0 ? "+" + Format(bytes) : Format(bytes);
}
