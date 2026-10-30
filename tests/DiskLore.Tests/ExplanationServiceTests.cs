using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Programs;
using DiskLore.Core.Rules;
using Xunit;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

public class ExplanationServiceTests
{
    private static ExplanationService Service(string rulesJson)
    {
        var rules = RuleSet.FromJson(rulesJson);
        var programs = new List<InstalledProgram>();
        return new ExplanationService(
            rules,
            new ProgramMatcher(programs),
            new LeftoverHeuristic(programs),
            new AiCache(Path.Combine(Path.GetTempPath(), "disklore-ai-" + Guid.NewGuid().ToString("N") + ".json")));
    }

    private const string Rules = """
        { "rules": [
          { "id": "big", "match": { "name": "Big" }, "inherit": true, "title": "Big store", "badge": "shrink",
            "owner": "Acme", "summary": "Shrink, never delete", "whatItIs": "w", "ifDeleted": "boom",
            "saferAlternative": "use the tool", "action": { "kind": "command", "label": "Clean", "value": "clean" } },
          { "id": "inner", "match": { "name": "Inner" }, "inherit": true, "title": "Inner thing", "badge": "check", "summary": "inner" },
          { "id": "leaf", "match": { "name": "Leaf" }, "title": "Leaf", "badge": "safe", "summary": "leaf" }
        ] }
        """;

    [Fact]
    public void RuleHit_ComesBackWithRuleSource()
    {
        var svc = Service(Rules);
        var e = svc.Explain(Dir(Root(AbsRoot), "Leaf"));
        Assert.Equal(ExplanationSource.Rule, e.Source);
        Assert.Equal(Badge.Safe, e.Badge);
        Assert.Equal("leaf", e.RuleId);
    }

    [Fact]
    public void Inheritance_DescendantsGetPartOfTitle()
    {
        var svc = Service(Rules);
        var big = Dir(Root(AbsRoot), "Big");
        var deep = Dir(Dir(Dir(big, "x"), "y"), "z");

        var e = svc.Explain(deep);

        Assert.Equal(ExplanationSource.Inherited, e.Source);
        Assert.Equal("Part of Big store", e.Title);
        Assert.Equal(Badge.Shrink, e.Badge);
        Assert.Equal("Acme", e.Owner);
        Assert.Equal("Shrink, never delete", e.Summary);
        Assert.Equal("w", e.WhatItIs);
        Assert.Equal("boom", e.IfDeleted);
        Assert.Equal("use the tool", e.SaferAlternative);
        Assert.Equal("Clean", e.Action!.Label);
    }

    [Fact]
    public void Inheritance_NearestInheritingAncestorWins_OwnRuleBeatsInheritance()
    {
        var svc = Service(Rules);
        var big = Dir(Root(AbsRoot), "Big");
        var inner = Dir(big, "Inner");
        var below = Dir(inner, "below");
        var leaf = Dir(big, "Leaf");

        Assert.Equal("Part of Inner thing", svc.Explain(below).Title);
        Assert.Equal(ExplanationSource.Rule, svc.Explain(inner).Source);
        Assert.Equal(ExplanationSource.Rule, svc.Explain(leaf).Source);
    }

    [Fact]
    public void Inheritance_NotInheritedFromRulesWithoutTheFlag()
    {
        var svc = Service(Rules);
        var below = Dir(Dir(Root(AbsRoot), "Leaf"), "child");
        Assert.Equal(ExplanationSource.None, svc.Explain(below).Source);
    }

    [Fact]
    public void Unknown_Fallback()
    {
        var svc = Service(Rules);
        var node = Dir(Root(AbsRoot), "Mystery");

        var e = svc.Explain(node);

        Assert.Equal("Mystery", e.Title);
        Assert.Equal(Badge.Unknown, e.Badge);
        Assert.Equal(ExplanationSource.None, e.Source);
        Assert.Equal("Not recognized yet", e.Summary);
        Assert.Equal("", e.WhatItIs);
        Assert.Equal("", e.WhyBig);
        Assert.Equal("", e.IfDeleted);
        Assert.Null(e.Action);
        Assert.Null(e.RuleId);
    }

    [Fact]
    public void Results_AreMemoized_UntilInvalidated_AndInvalidateRaisesEvent()
    {
        var svc = Service(Rules);
        var node = Dir(Root(AbsRoot), "Mystery");
        var raised = new List<ScanNode>();
        svc.ExplanationChanged += raised.Add;

        var first = svc.Explain(node);
        Assert.Same(first, svc.Explain(node));

        svc.Invalidate(node);

        Assert.Equal(new[] { node }, raised);
        Assert.NotSame(first, svc.Explain(node));
        Assert.Equal(first, svc.Explain(node));
    }

    [Fact]
    public void ThrowingOrStubSources_DoNotBreakExplain()
    {
        // ProgramMatcher / LeftoverHeuristic / AiCache may still throw NotImplementedException.
        var svc = Service(Rules);
        var root = Root(AbsRoot);
        for (int i = 0; i < 50; i++)
            Assert.Equal(Badge.Unknown, svc.Explain(Dir(root, "unknown" + i)).Badge);
        Assert.Equal(Badge.Safe, svc.Explain(Dir(root, "Leaf")).Badge);
    }

    [Fact]
    public void ConcurrentReads_AreSafe()
    {
        var svc = Service(Rules);
        var root = Root(AbsRoot);
        var nodes = new List<ScanNode>();
        var big = Dir(root, "Big");
        for (int i = 0; i < 200; i++) nodes.Add(Dir(i % 2 == 0 ? big : root, "n" + i));

        Parallel.For(0, 8, _ =>
        {
            foreach (var n in nodes) svc.Explain(n);
            foreach (var n in nodes.Take(20)) svc.Invalidate(n);
        });

        Assert.Equal(ExplanationSource.Inherited, svc.Explain(nodes[0]).Source);
        Assert.Equal(ExplanationSource.None, svc.Explain(nodes[1]).Source);
    }

    [Fact]
    public void AiAnswer_BeatsLeftoverGuess()
    {
        var local = Path.Combine(AbsRoot, "Users", "u", "AppData", "Local");
        var programs = new List<InstalledProgram>();
        var leftovers = new LeftoverHeuristic(programs, new[] { local }, null, true, () => DateTime.UtcNow);
        var ai = new AiCache(Path.Combine(Path.GetTempPath(), "disklore-ai-" + Guid.NewGuid().ToString("N") + ".json"));
        var svc = new ExplanationService(RuleSet.FromJson(Rules), new ProgramMatcher(programs), leftovers, ai);

        var root = Root(AbsRoot);
        var vendor = Dir(Dir(Dir(Dir(Dir(root, "Users"), "u"), "AppData"), "Local"), "FooVendor");
        var other = Dir(vendor.Parent!, "BarVendor");
        Assert.Equal(Badge.Leftover, svc.Explain(other).Badge);

        ai.Set(vendor.FullPath, new Explanation("Foo tool data", Badge.Check, "Foo", "sum", "w", "y", "d", null, null,
            ExplanationSource.AI, 0.8, null));
        var e = svc.Explain(vendor);
        Assert.Equal(ExplanationSource.AI, e.Source);
        Assert.Equal("Foo tool data", e.Title);
    }

    private const string TypeRules = """
        { "rules": [
          { "id": "sys", "match": { "name": "Sys" }, "inherit": true, "title": "System", "badge": "critical", "summary": "s" },
          { "id": "tmp", "match": { "name": "Tmp" }, "inherit": true, "title": "Temp", "badge": "safe", "summary": "s" },
          { "id": "docs", "match": { "name": "Docs" }, "inherit": true, "title": "Docs", "badge": "user", "summary": "s" },
          { "id": "wav", "match": { "extension": ".wav" }, "title": "Audio", "badge": "user", "summary": "s" },
          { "id": "vhdx", "match": { "extension": ".vhdx" }, "title": "Disk", "badge": "check", "summary": "s" },
          { "id": "named", "match": { "name": "chime.wav" }, "title": "Named", "badge": "safe", "summary": "s" }
        ] }
        """;

    [Fact]
    public void FileTypeRule_NeverLowersTheFolderVerdict()
    {
        var root = Root(AbsRoot);
        var game = Dir(root, "Game");
        var programs = new List<InstalledProgram> { new("Game", "Acme", game.FullPath, "1.0", null, false, null) };
        var svc = new ExplanationService(RuleSet.FromJson(TypeRules), new ProgramMatcher(programs),
            new LeftoverHeuristic(programs, Array.Empty<string>(), null, false, () => DateTime.UtcNow),
            new AiCache(Path.Combine(Path.GetTempPath(), "disklore-ai-" + Guid.NewGuid().ToString("N") + ".json")));

        // More cautious context wins over the type rule.
        var sysWav = svc.Explain(File(Dir(Dir(root, "Sys"), "Media"), "ding.wav", 1));
        Assert.Equal(Badge.Critical, sysWav.Badge);
        Assert.Equal(ExplanationSource.Inherited, sysWav.Source);
        var gameWav = svc.Explain(File(game, "intro.wav", 1));
        Assert.Equal(Badge.App, gameWav.Badge);
        Assert.Equal(ExplanationSource.InstalledProgram, gameWav.Source);

        // The type rule applies where it is at least as cautious, or where nothing else is known.
        Assert.Equal("wav", svc.Explain(File(Dir(root, "Docs"), "song.wav", 1)).RuleId);
        Assert.Equal("wav", svc.Explain(File(Dir(root, "Tmp"), "song.wav", 1)).RuleId);
        Assert.Equal("wav", svc.Explain(File(root, "loose.wav", 1)).RuleId);
        Assert.Equal("vhdx", svc.Explain(File(Dir(root, "Docs"), "vm.vhdx", 1)).RuleId);

        // Name and path rules are specific knowledge and are not second-guessed.
        Assert.Equal("named", svc.Explain(File(Dir(root, "Sys"), "chime.wav", 1)).RuleId);
    }
}
