using System.Reflection;
using System.Text.Json;

namespace DiskLore.Core.Rules;

public static class RuleLoader
{
    private static readonly object Gate = new();
    private static readonly List<string> SharedErrors = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Problems found by LoadEmbedded() and any LoadFile() calls after it. Cleared by LoadEmbedded().
    public static IReadOnlyList<string> Errors
    {
        get { lock (Gate) return SharedErrors.ToArray(); }
    }

    public static IReadOnlyList<RuleDto> LoadEmbedded()
    {
        lock (Gate) SharedErrors.Clear();
        var errors = new List<string>();
        var rules = LoadEmbedded(errors);
        Record(errors);
        return rules;
    }

    // Never throws: a missing or unreadable file yields an empty list (a missing file is not an error).
    public static IReadOnlyList<RuleDto> LoadFile(string path)
    {
        var errors = new List<string>();
        var rules = LoadFile(path, errors);
        Record(errors);
        return rules;
    }

    internal static List<RuleDto> LoadEmbedded(List<string> errors)
    {
        var all = new List<RuleDto>();
        var asm = typeof(RuleLoader).Assembly;
        var names = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Knowledge.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal);
        foreach (var name in names)
        {
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                all.AddRange(Parse(reader.ReadToEnd(), name, errors));
            }
            catch (Exception e)
            {
                errors.Add($"{name}: {e.Message}");
            }
        }
        return all;
    }

    internal static List<RuleDto> LoadFile(string path, List<string> errors)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<RuleDto>();
            return Parse(File.ReadAllText(path), path, errors);
        }
        catch (Exception e)
        {
            errors.Add($"{path}: {e.Message}");
            return new List<RuleDto>();
        }
    }

    // Each rule is deserialized and validated on its own so one bad entry never hides the rest.
    internal static List<RuleDto> Parse(string json, string source, List<string> errors)
    {
        var result = new List<RuleDto>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException e)
        {
            errors.Add($"{source}: invalid JSON ({e.Message})");
            return result;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(doc.RootElement, "rules", out var rules) ||
                rules.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{source}: expected an object with a \"rules\" array");
                return result;
            }

            int index = 0;
            foreach (var el in rules.EnumerateArray())
            {
                index++;
                RuleDto? dto;
                try
                {
                    dto = el.Deserialize<RuleDto>(Options);
                }
                catch (JsonException e)
                {
                    errors.Add($"{source}: rule #{index}: {e.Message}");
                    continue;
                }
                if (dto == null)
                {
                    errors.Add($"{source}: rule #{index} is empty");
                    continue;
                }
                var problem = RuleCompiler.Validate(dto);
                if (problem != null)
                {
                    errors.Add($"{source}: rule '{(string.IsNullOrEmpty(dto.Id) ? "#" + index : dto.Id)}': {problem}");
                    continue;
                }
                result.Add(dto);
            }
        }
        return result;
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static void Record(List<string> errors)
    {
        if (errors.Count == 0) return;
        lock (Gate) SharedErrors.AddRange(errors);
    }
}
