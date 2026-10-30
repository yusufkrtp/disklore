using DiskLore.Core.Explanations;

namespace DiskLore.Core.Rules;

internal enum RuleKind { Any, Folder, File }

// Precedence tiers: lower wins.
internal enum RuleTier { Path = 0, NameWithQualifiers = 1, Name = 2, Extension = 3 }

internal sealed class CompiledRule
{
    public required string Id { get; init; }
    public required RuleTier Tier { get; init; }
    public required int Priority { get; init; }
    public required int PatternLength { get; init; }
    public required RuleKind Kind { get; init; }
    public required bool Inherit { get; init; }
    public required Explanation Explanation { get; init; }

    public PathPattern? Path { get; init; }
    public string? Name { get; init; }            // literal or glob, as written
    public bool NameIsGlob { get; init; }
    public string? Extension { get; init; }       // lower-case with leading dot
    public string? Sibling { get; init; }
    public string? ParentName { get; init; }
    public PathPattern? Under { get; init; }
    public long? MinSize { get; init; }

    // Best first: tier, then higher priority, then longer pattern, then id for determinism.
    public static int Compare(CompiledRule a, CompiledRule b)
    {
        int c = a.Tier.CompareTo(b.Tier);
        if (c != 0) return c;
        c = b.Priority.CompareTo(a.Priority);
        if (c != 0) return c;
        c = b.PatternLength.CompareTo(a.PatternLength);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }
}

internal static class RuleCompiler
{
    private static readonly string[] ActionKinds = ["command", "settingsUri", "url", "openPath"];

    // Structural validation that does not depend on the current machine.
    public static string? Validate(RuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id)) return "missing id";
        if (string.IsNullOrWhiteSpace(dto.Title)) return "missing title";
        if (!Enum.TryParse<Badge>(dto.Badge, true, out _)) return $"unknown badge '{dto.Badge}'";
        if (dto.Kind != null && !TryKind(dto.Kind, out _)) return $"unknown kind '{dto.Kind}'";

        var m = dto.Match;
        int primaries = (string.IsNullOrWhiteSpace(m.Path) ? 0 : 1)
                        + (string.IsNullOrWhiteSpace(m.Name) ? 0 : 1)
                        + (string.IsNullOrWhiteSpace(m.Extension) ? 0 : 1);
        if (primaries != 1) return "match needs exactly one of path, name, extension";

        if (dto.Action != null)
        {
            if (!ActionKinds.Contains(dto.Action.Kind, StringComparer.Ordinal)) return $"unknown action kind '{dto.Action.Kind}'";
            if (string.IsNullOrWhiteSpace(dto.Action.Value)) return "action without value";
        }
        if (dto.Platforms != null)
            foreach (var p in dto.Platforms)
                if (!IsKnownPlatform(p)) return $"unknown platform '{p}'";
        return null;
    }

    public static bool AppliesToThisPlatform(RuleDto dto)
    {
        if (dto.Platforms == null || dto.Platforms.Count == 0) return true;
        string current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        return dto.Platforms.Any(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
    }

    // Returns null when the rule is invalid (error set) or does not apply to this platform (error null).
    public static CompiledRule? Compile(RuleDto dto, out string? error)
    {
        error = Validate(dto);
        if (error != null) return null;
        if (!AppliesToThisPlatform(dto)) return null;

        try
        {
            var m = dto.Match;
            var badge = Enum.Parse<Badge>(dto.Badge, true);
            TryKind(dto.Kind, out var kind);

            SaferAction? action = dto.Action == null ? null : new SaferAction(dto.Action.Kind, dto.Action.Label, dto.Action.Value);
            var explanation = new Explanation(
                dto.Title, badge, dto.Owner, dto.Summary, dto.WhatItIs, dto.WhyBig, dto.IfDeleted,
                dto.SaferAlternative, action, ExplanationSource.Rule, 1.0, dto.Id);

            bool hasQualifier = !string.IsNullOrWhiteSpace(m.Sibling) || !string.IsNullOrWhiteSpace(m.ParentName)
                                || !string.IsNullOrWhiteSpace(m.Under) || m.MinSize != null;

            RuleTier tier;
            PathPattern? path = null;
            string? name = null, ext = null;
            int len;
            if (!string.IsNullOrWhiteSpace(m.Path))
            {
                tier = RuleTier.Path;
                path = new PathPattern(m.Path);
                len = m.Path.Length;
            }
            else if (!string.IsNullOrWhiteSpace(m.Name))
            {
                tier = hasQualifier ? RuleTier.NameWithQualifiers : RuleTier.Name;
                name = m.Name.Trim();
                len = name.Length;
            }
            else
            {
                tier = RuleTier.Extension;
                ext = m.Extension!.Trim().ToLowerInvariant();
                if (!ext.StartsWith('.')) ext = "." + ext;
                len = ext.Length;
                if (kind == RuleKind.Any) kind = RuleKind.File;
            }

            return new CompiledRule
            {
                Id = dto.Id,
                Tier = tier,
                Priority = dto.Priority,
                PatternLength = len,
                Kind = kind,
                Inherit = dto.Inherit,
                Explanation = explanation,
                Path = path,
                Name = name,
                NameIsGlob = name != null && name.Contains('*'),
                Extension = ext,
                Sibling = NullIfBlank(m.Sibling),
                ParentName = NullIfBlank(m.ParentName),
                Under = string.IsNullOrWhiteSpace(m.Under) ? null : new PathPattern(m.Under),
                MinSize = m.MinSize,
            };
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool IsKnownPlatform(string p) =>
        p.Equals("windows", StringComparison.OrdinalIgnoreCase)
        || p.Equals("linux", StringComparison.OrdinalIgnoreCase)
        || p.Equals("macos", StringComparison.OrdinalIgnoreCase);

    private static bool TryKind(string? s, out RuleKind kind)
    {
        kind = RuleKind.Any;
        if (string.IsNullOrWhiteSpace(s) || s.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("folder", StringComparison.OrdinalIgnoreCase)) { kind = RuleKind.Folder; return true; }
        if (s.Equals("file", StringComparison.OrdinalIgnoreCase)) { kind = RuleKind.File; return true; }
        return false;
    }
}
