using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Rules;
using Xunit;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

public class RuleSetTests
{
    private static string Rule(string id, string match, string extra = "", string badge = "safe") =>
        $$"""{ "id": "{{id}}", "match": { {{match}} }, "title": "{{id}}", "badge": "{{badge}}", "summary": "s-{{id}}" {{(extra.Length > 0 ? "," + extra : "")}} }""";

    private static RuleSet Set(params string[] rules) =>
        RuleSet.FromJson("{ \"rules\": [" + string.Join(",", rules) + "] }");

    private static string Id(RuleSet set, ScanNode n) => set.Explain(n)?.RuleId ?? "";

    private static string Fwd(string s) => s.Replace('\\', '/');

    [Fact]
    public void Explain_BuildsRuleExplanation()
    {
        var set = Set("""
            { "id": "x.cache", "match": { "name": "cache" }, "kind": "folder", "title": "A cache", "badge": "SAFE",
              "owner": "Acme", "summary": "sum", "whatItIs": "what", "whyBig": "why", "ifDeleted": "regen",
              "saferAlternative": "alt", "action": { "kind": "command", "label": "Run", "value": "do it" } }
            """);
        var root = Root(AbsRoot);
        var e = set.Explain(Dir(root, "Cache"))!;

        Assert.Equal("A cache", e.Title);
        Assert.Equal(Badge.Safe, e.Badge);
        Assert.Equal("Acme", e.Owner);
        Assert.Equal("what", e.WhatItIs);
        Assert.Equal("regen", e.IfDeleted);
        Assert.Equal("alt", e.SaferAlternative);
        Assert.Equal(new SaferAction("command", "Run", "do it"), e.Action);
        Assert.Equal(ExplanationSource.Rule, e.Source);
        Assert.Equal(1.0, e.Confidence);
        Assert.Equal("x.cache", e.RuleId);
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public void Precedence_PathBeatsQualifiedNameBeatsNameBeatsExtension()
    {
        var root = Root(AbsRoot);
        var proj = Dir(root, "proj");
        var f = File(proj, "app.log", 10);

        var ext = Rule("ext", "\"extension\": \".log\"", "\"priority\": 999");
        var name = Rule("name", "\"name\": \"app.log\"");
        var nameQ = Rule("nameq", "\"name\": \"app.log\", \"parentName\": \"proj\"");
        var path = Rule("path", $"\"path\": \"{Fwd(AbsRoot)}/proj/app.log\"");

        Assert.Equal("ext", Id(Set(ext), f));
        Assert.Equal("name", Id(Set(ext, name), f));
        Assert.Equal("nameq", Id(Set(ext, name, nameQ), f));
        Assert.Equal("path", Id(Set(ext, name, nameQ, path), f));
    }

    [Fact]
    public void Precedence_PriorityThenLongerPattern()
    {
        var root = Root(AbsRoot);
        var f = File(root, "application.dat", 1);

        var shortGlob = Rule("short", "\"name\": \"app*\"");
        var longGlob = Rule("long", "\"name\": \"application*\"");
        Assert.Equal("long", Id(Set(shortGlob, longGlob), f));

        var boosted = Rule("boosted", "\"name\": \"app*\"", "\"priority\": 5");
        Assert.Equal("boosted", Id(Set(longGlob, boosted), f));
    }

    [Fact]
    public void Name_GlobAndCaseRules()
    {
        var root = Root(AbsRoot);
        var f = Dir(root, "MyCache_01");
        var set = Set(Rule("g", "\"name\": \"*cache_*\""));
        Assert.Equal(!OperatingSystem.IsLinux() ? "g" : "", Id(set, f));
        Assert.Equal("g", Id(set, Dir(root, "xcache_9")));
        Assert.Equal("", Id(set, Dir(root, "other")));
    }

    [Fact]
    public void Sibling_SupportsGlobs()
    {
        var root = Root(AbsRoot);
        var unity = Dir(root, "UnityGame");
        var lib = Dir(unity, "Library");
        File(unity, "Game.csproj", 1);
        var plain = Dir(root, "Plain");
        var lib2 = Dir(plain, "Library");
        File(plain, "readme.txt", 1);

        var set = Set(Rule("unity", "\"name\": \"Library\", \"sibling\": \"*.csproj\""));
        Assert.Equal("unity", Id(set, lib));
        Assert.Equal("", Id(set, lib2));
    }

    [Fact]
    public void ParentName_Under_MinSize_Kind()
    {
        var root = Root(AbsRoot);
        var steam = Dir(Dir(root, "Steam"), "steamapps");
        var common = Dir(steam, "common");
        var game = Dir(common, "Game");
        game.Size = 5000;
        var elsewhere = Dir(Dir(root, "Other"), "Game");
        elsewhere.Size = 5000;
        var small = Dir(common, "Game2");
        small.Size = 10;
        var gameFile = File(common, "Game", 6000);

        var parent = Set(Rule("p", "\"name\": \"Game\", \"parentName\": \"common\"", "\"kind\": \"folder\""));
        Assert.Equal("p", Id(parent, game));
        Assert.Equal("", Id(parent, elsewhere));
        Assert.Equal("", Id(parent, gameFile)); // kind: folder

        var under = Set(Rule("u", "\"name\": \"Game\", \"under\": \"**/steamapps\""));
        Assert.Equal("u", Id(under, game));
        Assert.Equal("", Id(under, elsewhere));

        var rooted = Set(Rule("u2", $"\"name\": \"Game\", \"under\": \"{Fwd(AbsRoot)}/Steam\""));
        Assert.Equal("u2", Id(rooted, game));

        var minSize = Set(Rule("m", "\"name\": \"Game*\", \"minSize\": 1000"));
        Assert.Equal("m", Id(minSize, game));
        Assert.Equal("", Id(minSize, small));

        var fileOnly = Set(Rule("f", "\"name\": \"Game\"", "\"kind\": \"file\""));
        Assert.Equal("f", Id(fileOnly, gameFile));
        Assert.Equal("", Id(fileOnly, game));
    }

    [Fact]
    public void ExtensionRules_OnlyMatchFiles_CaseInsensitively()
    {
        var root = Root(AbsRoot);
        var set = Set(Rule("vm", "\"extension\": \"VHDX\""));
        Assert.Equal("vm", Id(set, File(root, "disk.VHDX", 1)));
        Assert.Equal("", Id(set, Dir(root, "disk.vhdx")));
        Assert.Equal("", Id(set, File(root, "disk.vhd", 1)));
    }

    [Fact]
    public void Platforms_FilterAtLoad()
    {
        string current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var others = new[] { "windows", "linux", "macos" }.Where(p => p != current).Select(p => $"\"{p}\"");
        var root = Root(AbsRoot);
        var node = Dir(root, "thing");

        var set = Set(
            Rule("other", "\"name\": \"thing\"", $"\"platforms\": [{string.Join(",", others)}]", "check"),
            Rule("mine", "\"name\": \"thing\"", $"\"platforms\": [\"{current.ToUpperInvariant()}\"]", "check"));
        Assert.Equal(1, set.Count);
        Assert.Equal("mine", Id(set, node));
        Assert.Empty(set.Errors);
    }

    [Fact]
    public void WindowsPathRule_OnlyOnWindows()
    {
        var set = Set(Rule("sxs", "\"path\": \"%WINDIR%\\\\WinSxS\"", "\"platforms\": [\"windows\"]", "shrink"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(0, set.Count);
            return;
        }
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = Root(Path.GetPathRoot(win)!);
        var node = Dir(Dir(root, Path.GetFileName(win)), "WinSxS");
        Assert.Equal("sxs", Id(set, node));
        Assert.Equal(Badge.Shrink, set.Explain(node)!.Badge);
        Assert.Equal("", Id(set, Dir(node, "child")));
    }

    [Fact]
    public void InvalidRules_AreSkippedAndRecorded()
    {
        var set = Set(
            Rule("good", "\"name\": \"a\""),
            Rule("badbadge", "\"name\": \"b\"", "", "purple"),
            Rule("nomatch", ""),
            Rule("two", "\"name\": \"a\", \"extension\": \".x\""),
            Rule("badtoken", "\"path\": \"%NOPE%/x\""),
            "{ \"match\": { \"name\": \"noid\" }, \"title\": \"t\", \"badge\": \"safe\" }",
            "{ \"id\": \"wrongtype\", \"priority\": \"high\", \"match\": { \"name\": \"w\" } }");

        Assert.Equal(1, set.Count);
        Assert.Equal(6, set.Errors.Count);
    }

    [Fact]
    public void BrokenJson_DoesNotThrow()
    {
        var set = RuleSet.FromJson("{ not json");
        Assert.Equal(0, set.Count);
        Assert.Single(set.Errors);
    }

    [Fact]
    public void UserFile_OverridesEmbeddedOnEqualId_AndMissingFileIsFine()
    {
        var a = RuleLoader.LoadEmbedded(); // whatever is embedded right now
        Assert.NotNull(a);
        Assert.Empty(RuleLoader.LoadFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json")));

        var dtos = new List<RuleDto>();
        var errors = new List<string>();
        dtos.AddRange(RuleLoader.Parse("{ \"rules\": [" + Rule("same", "\"name\": \"zed\"", "", "safe") + "] }", "base", errors));
        dtos.AddRange(RuleLoader.Parse("{ \"rules\": [" + Rule("same", "\"name\": \"zed\"", "", "check") + "] }", "user", errors));
        var set = RuleSet.FromDtos(dtos, errors);

        Assert.Equal(1, set.Count);
        Assert.Equal(Badge.Check, set.Explain(Dir(Root(AbsRoot), "zed"))!.Badge);
    }

    [Fact]
    public void LoadFile_ReadsRulesFromDisk()
    {
        var file = Path.Combine(Path.GetTempPath(), "disklore-rules-" + Guid.NewGuid().ToString("N") + ".json");
        File2.Write(file, "{ \"rules\": [" + Rule("disk", "\"name\": \"q\"") + "] }");
        try
        {
            var set = RuleSet.Load(file);
            Assert.Equal("disk", Id(set, Dir(Root(AbsRoot), "q")));
        }
        finally { System.IO.File.Delete(file); }
    }

    [Fact]
    public void Inherits_ReflectsRuleFlag()
    {
        var set = Set(Rule("yes", "\"name\": \"y\"", "\"inherit\": true"), Rule("no", "\"name\": \"n\""));
        Assert.True(set.Inherits("yes"));
        Assert.False(set.Inherits("no"));
        Assert.False(set.Inherits("missing"));
    }

    [Fact]
    public void EmbeddedRules_LoadWithoutErrors()
    {
        var set = RuleSet.Load(null);
        Assert.Empty(set.Errors);
    }

    private static class File2
    {
        public static void Write(string path, string text) => System.IO.File.WriteAllText(path, text);
    }
}
