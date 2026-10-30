using System.Text.Json;
using System.Text.Json.Nodes;
using DiskLore.Core.Explanations;

namespace DiskLore.Core.Ai;

public sealed class AiCache
{
    private const string ProfileToken = "%USERPROFILE%";

    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private bool _loaded;

    private sealed class CacheEntry
    {
        public string Title { get; set; } = "";
        public string Badge { get; set; } = "check";
        public string? Owner { get; set; }
        public string Summary { get; set; } = "";
        public string WhatItIs { get; set; } = "";
        public string WhyBig { get; set; } = "";
        public string IfDeleted { get; set; } = "";
        public string? SaferAlternative { get; set; }
        public double Confidence { get; set; }
        public DateTime AskedUtc { get; set; }
    }

    private sealed class CacheFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, CacheEntry> Entries { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public AiCache(string filePath)
    {
        _filePath = filePath;
    }

    // Lookup by normalized path.
    public Explanation? Get(string fullPath)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (!_entries.TryGetValue(Normalize(fullPath), out var e)) return null;
            return ToExplanation(e);
        }
    }

    public void Set(string fullPath, Explanation e)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _entries[Normalize(fullPath)] = new CacheEntry
            {
                Title = e.Title,
                Badge = e.Badge.ToString().ToLowerInvariant(),
                Owner = e.Owner,
                Summary = e.Summary,
                WhatItIs = e.WhatItIs,
                WhyBig = e.WhyBig,
                IfDeleted = e.IfDeleted,
                SaferAlternative = e.SaferAlternative,
                Confidence = e.Confidence,
                AskedUtc = DateTime.UtcNow,
            };
            Save();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _loaded = true;
            _entries.Clear();
            Save();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _entries.Count;
            }
        }
    }

    // Full path, trailing separator trimmed, user profile replaced by a token, lower-cased on Windows/macOS.
    internal static string Normalize(string fullPath)
    {
        var p = fullPath.Trim();
        while (p.Length > 1 && (p[^1] == '\\' || p[^1] == '/'))
        {
            // Keep a drive root like "C:\" intact.
            if (p.Length == 3 && p[1] == ':') break;
            p = p[..^1];
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        var ignoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (profile.Length > 0 && p.StartsWith(profile, cmp)
            && (p.Length == profile.Length || p[profile.Length] == '\\' || p[profile.Length] == '/'))
            p = ProfileToken + p[profile.Length..];

        if (OperatingSystem.IsWindows()) p = p.Replace('/', '\\');
        return ignoreCase ? p.ToLowerInvariant() : p;
    }

    private static Explanation ToExplanation(CacheEntry e)
    {
        var badge = Enum.TryParse<Badge>(e.Badge, true, out var b) ? b : Badge.Check;
        return new Explanation(
            e.Title, badge, e.Owner, e.Summary, e.WhatItIs, e.WhyBig, e.IfDeleted, e.SaferAlternative,
            null, ExplanationSource.AI, e.Confidence, null);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_filePath)) return;
        try
        {
            var text = File.ReadAllText(_filePath);
            var file = JsonSerializer.Deserialize<CacheFile>(text, JsonOptions);
            if (file == null || file.Version != 1) throw new JsonException("Unsupported cache file");
            foreach (var (k, v) in file.Entries)
                if (v != null) _entries[k] = v;
        }
        catch (Exception)
        {
            _entries.Clear();
            try
            {
                var bad = _filePath + ".bad";
                File.Move(_filePath, bad, overwrite: true);
            }
            catch (Exception)
            {
                // Could not move it aside; the next Save overwrites the corrupt file.
            }
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new CacheFile { Entries = new Dictionary<string, CacheEntry>(_entries) }, JsonOptions);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception)
        {
            // The cache is an optimization; a failed save must not break the explainer.
        }
    }
}
