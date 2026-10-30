using System.Runtime.CompilerServices;
using DiskLore.Core.Ai;
using DiskLore.Core.Model;
using DiskLore.Core.Programs;
using DiskLore.Core.Rules;

namespace DiskLore.Core.Explanations;

public sealed class ExplanationService
{
    private readonly RuleSet _rules;
    private readonly ProgramMatcher _programs;
    private readonly LeftoverHeuristic _leftovers;
    private readonly AiCache _aiCache;

    // Final answers per node, and rule-only answers per node for the inheritance walk.
    private readonly ConditionalWeakTable<ScanNode, Explanation> _memo = new();
    private readonly ConditionalWeakTable<ScanNode, RuleHit> _ruleHits = new();

    // A source whose implementation is still a stub is skipped from then on instead of
    // paying for an exception on every row.
    private bool _programsStub, _leftoversStub, _aiStub;

    private sealed class RuleHit
    {
        public RuleHit(Explanation? explanation) => Explanation = explanation;
        public readonly Explanation? Explanation;
    }

    // Order: RuleSet -> ProgramMatcher -> AiCache -> LeftoverHeuristic -> Inherited -> Unknown.
    // Exception: an `extension` rule yields to a more cautious ProgramMatcher or Inherited answer.
    public ExplanationService(RuleSet rules, ProgramMatcher programs, LeftoverHeuristic leftovers, AiCache aiCache)
    {
        _rules = rules;
        _programs = programs;
        _leftovers = leftovers;
        _aiCache = aiCache;
    }

    public Explanation Explain(ScanNode node)
    {
        if (_memo.TryGetValue(node, out var cached)) return cached;
        var result = Compute(node);
        _memo.AddOrUpdate(node, result);
        return result;
    }

    public void Invalidate(ScanNode node)
    {
        _memo.Remove(node);
        _ruleHits.Remove(node);
        ExplanationChanged?.Invoke(node);
    }

    public event Action<ScanNode>? ExplanationChanged;

    private Explanation Compute(ScanNode node)
    {
        var own = RuleHitOf(node).Explanation;
        Explanation? e;
        if (own != null)
        {
            if (own.RuleId == null || !_rules.IsFileTypeRule(own.RuleId)) return own;

            // A file-type rule only knows the extension. When the file sits in an installed program or in a
            // folder whose rule is inherited, that context wins if it is more cautious: a type rule may warn
            // about a file, but never lowers the verdict of the folder it lives in.
            var context = Guard(() => _programs?.Explain(node), ref _programsStub) ?? Inherit(node);
            return context != null && Caution(context.Badge) > Caution(own.Badge) ? context : own;
        }

        e = Guard(() => _programs?.Explain(node), ref _programsStub);
        if (e != null) return e;

        // An answer the user asked Claude for beats the leftover guess.
        e = Guard(() => _aiCache?.Get(node.FullPath), ref _aiStub);
        if (e != null) return e;

        e = Guard(() => _leftovers?.Explain(node), ref _leftoversStub);
        if (e != null) return e;

        e = Inherit(node);
        if (e != null) return e;

        return new Explanation(node.Name, Badge.Unknown, null, "Not recognized yet", "", "", "", null, null,
            ExplanationSource.None, 0, null);
    }

    private Explanation? Inherit(ScanNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            var hit = RuleHitOf(p).Explanation;
            if (hit?.RuleId == null || !_rules.Inherits(hit.RuleId)) continue;

            return new Explanation(
                "Part of " + hit.Title, hit.Badge, hit.Owner, hit.Summary, hit.WhatItIs, hit.WhyBig,
                hit.IfDeleted, hit.SaferAlternative, hit.Action, ExplanationSource.Inherited,
                hit.Confidence, hit.RuleId);
        }
        return null;
    }

    // Higher means "be more careful with it".
    private static int Caution(Badge badge) => badge switch
    {
        Badge.Critical => 6,
        Badge.Shrink => 5,
        Badge.App => 4,
        Badge.Check => 3,
        Badge.User => 2,
        Badge.Leftover => 1,
        _ => 0,
    };

    private RuleHit RuleHitOf(ScanNode node)
    {
        if (_ruleHits.TryGetValue(node, out var cached)) return cached;
        Explanation? e = null;
        try { e = _rules?.Explain(node); } catch { }
        var hit = new RuleHit(e);
        _ruleHits.AddOrUpdate(node, hit);
        return hit;
    }

    private static Explanation? Guard(Func<Explanation?> source, ref bool stubFlag)
    {
        if (stubFlag) return null;
        try
        {
            return source();
        }
        catch (NotImplementedException)
        {
            stubFlag = true;
            return null;
        }
        catch
        {
            return null;
        }
    }
}
