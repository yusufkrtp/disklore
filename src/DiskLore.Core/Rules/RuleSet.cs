using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Rules;

public sealed class RuleSet : IExplainer
{
    private static readonly StringComparer NameComparer =
        PathPattern.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // Indexes. Every list is sorted best-first, so the first match in a list is that list's winner.
    private readonly Dictionary<string, Entry> _byLastName;
    private readonly Dictionary<string, CompiledRule[]> _byExtension;
    private readonly CompiledRule[] _pathGeneral;
    private readonly CompiledRule[] _nameGlobs;
    private readonly Dictionary<string, CompiledRule> _byId;

    private sealed class Entry
    {
        public List<CompiledRule> Rules = new();   // literal-name rules and path rules ending in this name
        public CompiledRule[] Sorted = Array.Empty<CompiledRule>();
    }

    private RuleSet(IEnumerable<CompiledRule> rules, IReadOnlyList<string> errors)
    {
        Errors = errors;
        _byId = new Dictionary<string, CompiledRule>(StringComparer.Ordinal);
        foreach (var r in rules) _byId[r.Id] = r; // later wins: the user file overrides on equal id

        _byLastName = new Dictionary<string, Entry>(NameComparer);
        var ext = new Dictionary<string, List<CompiledRule>>(StringComparer.OrdinalIgnoreCase);
        var general = new List<CompiledRule>();
        var globs = new List<CompiledRule>();

        foreach (var r in _byId.Values)
        {
            if (r.Tier == RuleTier.Extension)
            {
                if (!ext.TryGetValue(r.Extension!, out var l)) ext[r.Extension!] = l = new List<CompiledRule>();
                l.Add(r);
            }
            else if (r.Path != null)
            {
                var last = r.Path.LastLiteral;
                if (last == null) general.Add(r);
                else GetEntry(last).Rules.Add(r);
            }
            else if (r.NameIsGlob) globs.Add(r);
            else GetEntry(r.Name!).Rules.Add(r);
        }

        foreach (var e in _byLastName.Values)
        {
            e.Rules.Sort(CompiledRule.Compare);
            e.Sorted = e.Rules.ToArray();
        }
        _byExtension = ext.ToDictionary(kv => kv.Key, kv => Sorted(kv.Value), StringComparer.OrdinalIgnoreCase);
        _pathGeneral = Sorted(general);
        _nameGlobs = Sorted(globs);
        Count = _byId.Count;
    }

    private Entry GetEntry(string name)
    {
        if (!_byLastName.TryGetValue(name, out var e)) _byLastName[name] = e = new Entry();
        return e;
    }

    private static CompiledRule[] Sorted(List<CompiledRule> l)
    {
        l.Sort(CompiledRule.Compare);
        return l.ToArray();
    }

    // Problems found while loading/compiling (skipped rules); empty when everything was valid.
    public IReadOnlyList<string> Errors { get; }

    public int Count { get; }

    public static RuleSet Load(string? userRulesPath)
    {
        var errors = new List<string>();
        var dtos = RuleLoader.LoadEmbedded(errors);
        if (!string.IsNullOrEmpty(userRulesPath)) dtos.AddRange(RuleLoader.LoadFile(userRulesPath, errors));
        return FromDtos(dtos, errors);
    }

    internal static RuleSet FromJson(string json, string source = "inline")
    {
        var errors = new List<string>();
        var dtos = RuleLoader.Parse(json, source, errors);
        return FromDtos(dtos, errors);
    }

    internal static RuleSet FromDtos(IEnumerable<RuleDto> dtos, List<string>? errors = null)
    {
        errors ??= new List<string>();
        var compiled = new List<CompiledRule>();
        foreach (var dto in dtos)
        {
            var c = RuleCompiler.Compile(dto, out var error);
            if (c != null) compiled.Add(c);
            else if (error != null) errors.Add($"rule '{dto.Id}': {error}");
        }
        return new RuleSet(compiled, errors);
    }

    internal bool Inherits(string ruleId) => _byId.TryGetValue(ruleId, out var r) && r.Inherit;

    public Explanation? Explain(ScanNode node) => Find(node)?.Explanation;

    internal CompiledRule? Find(ScanNode node)
    {
        var ctx = new Ctx(node);
        CompiledRule? best = null;
        var name = Ctx.NameOf(node);

        if (_byLastName.TryGetValue(name, out var entry)) Scan(entry.Sorted, ref ctx, ref best);
        if (_pathGeneral.Length > 0) Scan(_pathGeneral, ref ctx, ref best);
        if (_nameGlobs.Length > 0) Scan(_nameGlobs, ref ctx, ref best);
        if (!node.IsDirectory && _byExtension.Count > 0)
        {
            var ext = node.Extension;
            if (ext.Length > 0 && _byExtension.TryGetValue(ext, out var byExt)) Scan(byExt, ref ctx, ref best);
        }
        return best;
    }

    private static void Scan(CompiledRule[] list, ref Ctx ctx, ref CompiledRule? best)
    {
        foreach (var rule in list)
        {
            if (best != null && CompiledRule.Compare(rule, best) >= 0) return; // sorted: nothing later can win
            if (Matches(rule, ref ctx))
            {
                best = rule;
                return;
            }
        }
    }

    private static bool Matches(CompiledRule r, ref Ctx ctx)
    {
        var node = ctx.Node;
        if (r.Kind == RuleKind.Folder && !node.IsDirectory) return false;
        if (r.Kind == RuleKind.File && node.IsDirectory) return false;
        if (r.MinSize is long min && node.Size < min) return false;
        if (r.NameIsGlob && !PathPattern.GlobMatch(r.Name!, Ctx.NameOf(node))) return false;

        if (r.ParentName != null)
        {
            var parent = node.Parent;
            if (parent == null || !PathPattern.GlobMatch(r.ParentName, Ctx.NameOf(parent))) return false;
        }

        if (r.Sibling != null)
        {
            var parent = node.Parent;
            if (parent == null) return false;
            bool found = false;
            foreach (var s in parent.Children)
            {
                if (ReferenceEquals(s, node)) continue;
                if (PathPattern.GlobMatch(r.Sibling, s.Name)) { found = true; break; }
            }
            if (!found) return false;
        }

        if (r.Path != null)
        {
            ctx.Build();
            if (!r.Path.Match(ctx.Segments!, ctx.Count)) return false;
        }
        if (r.Under != null)
        {
            ctx.Build();
            if (!r.Under.MatchesAncestorOf(ctx.Segments!, ctx.Count)) return false;
        }
        return true;
    }

    // Per-call state: path segments are only built when a rule actually needs them.
    private struct Ctx
    {
        public readonly ScanNode Node;
        public string[]? Segments;
        public int Count;

        public Ctx(ScanNode node)
        {
            Node = node;
            Segments = null;
            Count = 0;
        }

        public void Build()
        {
            if (Segments != null) return;
            Count = PathPattern.FillSegments(Node, out var buf);
            Segments = buf;
        }

        // Root nodes carry the full root path as their name; rules care about the last component.
        public static string NameOf(ScanNode node)
        {
            if (node.Parent != null) return node.Name;
            var n = node.Name.AsSpan().TrimEnd("\\/");
            int i = n.LastIndexOfAny('\\', '/');
            return i < 0 ? node.Name : n[(i + 1)..].ToString();
        }
    }
}
