using System.Text.Json;
using DiskLore.Core.Explanations;

namespace DiskLore.Core.Ai;

public static class AiResponseParser
{
    internal const int MaxSummaryLength = 90;

    // Parses the model output into path -> explanation (keyed by the path the model echoed), applying the safety clamp.
    public static IReadOnlyDictionary<string, Explanation> Parse(string output)
    {
        var result = new Dictionary<string, Explanation>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ParseEntries(output))
        {
            if (!string.IsNullOrWhiteSpace(entry.Path))
                result[entry.Path] = Clamp(entry.Explanation, entry.Path);
        }
        return result;
    }

    // Matches answers back to the request: first by path, then (for answers whose path was not recognised) by order.
    // `expected` holds (path as sent to the model, full path of the node); the result is keyed by full path.
    public static IReadOnlyDictionary<string, Explanation> Parse(
        string output, IReadOnlyList<(string RequestPath, string FullPath)> expected)
    {
        var result = new Dictionary<string, Explanation>(StringComparer.OrdinalIgnoreCase);
        var entries = ParseEntries(output, expected);
        var usedExpected = new bool[expected.Count];
        var unmatched = new List<(int Index, Entry Entry)>();

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var idx = -1;
            if (!string.IsNullOrWhiteSpace(e.Path))
            {
                var key = NormalizeForMatch(e.Path);
                for (var j = 0; j < expected.Count; j++)
                {
                    if (usedExpected[j]) continue;
                    if (NormalizeForMatch(expected[j].RequestPath) == key || NormalizeForMatch(expected[j].FullPath) == key)
                    {
                        idx = j;
                        break;
                    }
                }
            }
            if (idx < 0) unmatched.Add((i, e));
            else
            {
                usedExpected[idx] = true;
                result[expected[idx].FullPath] = Finalize(e, expected[idx].FullPath);
            }
        }

        // Fallback by order: an unmatched answer at position i takes slot i when that slot is still free.
        foreach (var (i, e) in unmatched)
        {
            if (i < expected.Count && !usedExpected[i])
            {
                usedExpected[i] = true;
                result[expected[i].FullPath] = Finalize(e, expected[i].FullPath);
            }
        }
        return result;
    }

    private sealed record Entry(string? Path, Explanation Explanation);

    private static Explanation Finalize(Entry e, string fullPath) => Clamp(e.Explanation, fullPath);

    private static string NormalizeForMatch(string p)
        => p.Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    private static List<Entry> ParseEntries(string output, IReadOnlyList<(string RequestPath, string FullPath)>? expected = null)
    {
        var list = new List<Entry>();
        if (string.IsNullOrWhiteSpace(output)) return list;
        try
        {
            var text = UnwrapEnvelope(output);
            var element = FindJsonPayload(text);
            if (element is not { } root) return list;

            IEnumerable<JsonElement> items = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object when root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array => arr.EnumerateArray().ToList(),
                JsonValueKind.Object => new[] { root },
                _ => Array.Empty<JsonElement>(),
            };
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var parsed = ParseItem(item);
                if (parsed != null) list.Add(parsed);
            }
        }
        catch
        {
            // Invalid answers are dropped, never thrown.
        }
        return list;
    }

    // `claude --output-format json` prints an object with a "result" string; accept plain model text too.
    private static string UnwrapEnvelope(string output)
    {
        var trimmed = output.Trim();
        if (!trimmed.StartsWith('{')) return output;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True)
                    return "";
                if (doc.RootElement.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
                    return r.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }
        return output;
    }

    private static JsonElement? FindJsonPayload(string text)
    {
        // 1. Fenced block.
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        while (fence >= 0)
        {
            var bodyStart = text.IndexOf('\n', fence);
            if (bodyStart < 0) break;
            var end = text.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (end < 0) break;
            var parsed = TryParse(text[(bodyStart + 1)..end]);
            if (parsed != null) return parsed;
            fence = text.IndexOf("```", end + 3, StringComparison.Ordinal);
        }
        // 2. Whole text.
        var whole = TryParse(text);
        if (whole != null) return whole;
        // 3. First '[' to last ']' (leading/trailing prose), then first '{' to last '}'.
        var a = text.IndexOf('[');
        var b = text.LastIndexOf(']');
        if (a >= 0 && b > a)
        {
            var p = TryParse(text[a..(b + 1)]);
            if (p != null) return p;
        }
        a = text.IndexOf('{');
        b = text.LastIndexOf('}');
        if (a >= 0 && b > a) return TryParse(text[a..(b + 1)]);
        return null;
    }

    private static JsonElement? TryParse(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s.Trim(), new JsonDocumentOptions { AllowTrailingCommas = true });
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement o, string name)
    {
        foreach (var p in o.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var s = p.Value.GetString()?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            return null;
        }
        return null;
    }

    private static Entry? ParseItem(JsonElement o)
    {
        var badge = MapBadge(Str(o, "badge"));
        if (badge == null) return null;

        var path = Str(o, "path");
        var whatItIs = Str(o, "whatItIs");
        var summary = Str(o, "summary") ?? whatItIs;
        if (summary == null) return null;

        var confidence = 0.5;
        foreach (var p in o.EnumerateObject())
        {
            if (!string.Equals(p.Name, "confidence", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var d)) confidence = d;
            break;
        }

        var title = Str(o, "title") ?? LastSegment(path) ?? "Unnamed item";
        var explanation = new Explanation(
            title, badge.Value, Str(o, "owner"), summary,
            whatItIs ?? summary,
            Str(o, "whyBig") ?? "",
            Str(o, "ifDeleted") ?? "",
            Str(o, "saferAlternative"),
            null,
            ExplanationSource.AI,
            confidence,
            null);
        return new Entry(path, explanation);
    }

    private static string? LastSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var t = path.TrimEnd('\\', '/');
        var i = t.LastIndexOfAny(['\\', '/']);
        var s = i >= 0 ? t[(i + 1)..] : t;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    // Critical and Shrink are rule-only; the model may say them, we turn them into Check.
    private static Badge? MapBadge(string? s)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "app": return Badge.App;
            case "check": return Badge.Check;
            case "safe": return Badge.Safe;
            case "leftover": return Badge.Leftover;
            case "user": return Badge.User;
            case "unknown": return Badge.Unknown;
            case "critical": return Badge.Critical;
            case "shrink": return Badge.Shrink;
            default: return null;
        }
    }

    private static readonly string[] SystemRootFiles =
        ["pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "$recycle.bin", "system volume information", "recovery"];

    // Safety clamp: never Critical/Shrink; never Safe for system locations; confidence 0.1-0.9; summary <= 90 chars.
    public static Explanation Clamp(Explanation e, string path)
    {
        var badge = e.Badge;
        if (badge is Badge.Critical or Badge.Shrink) badge = Badge.Check;
        if (badge == Badge.Safe && IsProtectedLocation(path)) badge = Badge.Check;

        var summary = e.Summary.Trim();
        if (summary.Length > MaxSummaryLength) summary = summary[..(MaxSummaryLength - 1)].TrimEnd() + "…";

        var confidence = double.IsNaN(e.Confidence) ? 0.5 : Math.Clamp(e.Confidence, 0.1, 0.9);
        return e with
        {
            Badge = badge,
            Summary = summary,
            Confidence = confidence,
            Source = ExplanationSource.AI,
            Action = null,
            RuleId = null,
        };
    }

    internal static bool IsProtectedLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Contains('%') ? Environment.ExpandEnvironmentVariables(path) : path;
        p = p.Replace('/', '\\').TrimEnd('\\');

        if (OperatingSystem.IsWindows() || p.Length > 1 && p[1] == ':')
        {
            foreach (var dir in ProtectedDirs())
            {
                if (IsUnder(p, dir)) return true;
            }
            // Loose files in the drive root (pagefile.sys, hiberfil.sys, ...).
            if (p.Length > 3 && p[1] == ':' && p[2] == '\\' && p.IndexOf('\\', 3) < 0)
            {
                var name = p[3..].ToLowerInvariant();
                if (SystemRootFiles.Contains(name) || name.EndsWith(".sys", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        foreach (var dir in new[] { "/usr", "/bin", "/sbin", "/lib", "/lib64", "/etc", "/boot", "/System", "/Library", "/private/var/db" })
        {
            var q = p.Replace('\\', '/');
            if (q.Equals(dir, StringComparison.Ordinal) || q.StartsWith(dir + "/", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static IEnumerable<string> ProtectedDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[]
        {
            Environment.GetEnvironmentVariable("WINDIR"),
            Environment.GetEnvironmentVariable("SystemRoot"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramW6432"),
            @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
        })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var d = raw.Replace('/', '\\').TrimEnd('\\');
            if (seen.Add(d)) yield return d;
        }
    }

    private static bool IsUnder(string path, string dir)
        => path.Equals(dir, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase);
}
