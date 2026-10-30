using System.Text.Json;
using DiskLore.Core.Model;
using DiskLore.Core.Rules;
using Xunit;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

// The engine features the embedded knowledge base relies on, checked against the real rule files.
public class KnowledgeBaseTests
{
    private static readonly RuleSet Kb = RuleSet.Load(null);

    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string UsersRoot => Path.GetDirectoryName(Profile.TrimEnd('\\', '/'))!;
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static string Id(ScanNode n) => Kb.Explain(n)?.RuleId ?? "";

    private static string Id(RuleSet set, ScanNode n) => set.Explain(n)?.RuleId ?? "";

    // Builds the directory chain for an absolute path and returns the last node (a file when size is given for one).
    private static ScanNode At(string fullPath, bool file = false, long size = 0)
    {
        var parts = fullPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        bool win = OperatingSystem.IsWindows();
        ScanNode node = Root(win ? parts[0] + "\\" : "/");
        for (int i = win ? 1 : 0; i < parts.Length; i++)
            node = file && i == parts.Length - 1 ? File(node, parts[i], size) : Dir(node, parts[i]);
        if (!file) node.Size = size;
        return node;
    }

    private static string J(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void EmbeddedRules_LoadCompletely_NothingDropped()
    {
        RuleLoader.LoadEmbedded();
        Assert.Empty(RuleLoader.Errors);
        Assert.Empty(Kb.Errors);

        string current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var asm = typeof(RuleSet).Assembly;
        int expected = 0, files = 0;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.Contains(".Knowledge.") && n.EndsWith(".json")))
        {
            files++;
            using var stream = asm.GetManifestResourceStream(name)!;
            using var doc = JsonDocument.Parse(stream);
            foreach (var rule in doc.RootElement.GetProperty("rules").EnumerateArray())
            {
                bool applies = !rule.TryGetProperty("platforms", out var p) || p.ValueKind == JsonValueKind.Null
                               || p.GetArrayLength() == 0
                               || p.EnumerateArray().Any(x => string.Equals(x.GetString(), current, StringComparison.OrdinalIgnoreCase));
                if (applies) expected++;
            }
        }
        Assert.True(files >= 8, $"only {files} knowledge files embedded");
        Assert.Equal(expected, Kb.Count);
    }

    [Fact]
    public void RuleIds_AreUniqueAcrossFiles_AndSummariesAreShort()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dto in RuleLoader.LoadEmbedded())
        {
            Assert.True(ids.Add(dto.Id), $"duplicate id {dto.Id}");
            Assert.True(dto.Summary.Length is > 0 and <= 90, $"{dto.Id}: summary length {dto.Summary.Length}");
        }
    }

    [Fact]
    public void EmbeddedRules_ForThisPlatform_AllCompile()
    {
        var dtos = RuleLoader.LoadEmbedded().ToList();
        var expected = dtos.Count(RuleCompiler.AppliesToThisPlatform);
        var errors = new List<string>();
        var set = RuleSet.FromDtos(dtos, errors);
        Assert.Empty(errors);
        Assert.Equal(expected, set.Count);
    }

    [Fact]
    public void UnixRules_CompileOnAnyPlatform()
    {
        // Rules without Windows-style paths must compile everywhere; this catches typos in the Unix files on Windows too.
        var dtos = RuleLoader.LoadEmbedded()
            .Where(d => !RuleCompiler.IsWindowsPattern(d.Match?.Path) && !RuleCompiler.IsWindowsPattern(d.Match?.Under))
            .Select(d => { d.Platforms = null; return d; }).ToList();
        var errors = new List<string>();
        var all = RuleSet.FromDtos(dtos, errors);
        Assert.Empty(errors);
        Assert.Equal(dtos.Count, all.Count);
    }

    [Theory]
    [InlineData(@"%PROGRAMFILES%\Foo", true)]
    [InlineData(@"%LOCALAPPDATA%/pip", true)]
    [InlineData(@"C:\Riot Games", true)]
    [InlineData("~/.cache", false)]
    [InlineData(@"**\node_modules", false)]
    [InlineData("%HOME%/Library/Caches", false)]
    [InlineData(null, false)]
    public void WindowsPatterns_AreDetected(string? pattern, bool windows) =>
        Assert.Equal(windows, RuleCompiler.IsWindowsPattern(pattern));

    // 1. '*' inside a segment.
    [Fact]
    public void StarInsideSegment()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal("user.electron-updaters", Id(At(J(LocalAppData, "notion-updater"))));
        Assert.NotEqual("user.electron-updaters", Id(At(J(LocalAppData, "updater"))));
        Assert.Equal("dev.wsl-store-vhdx",
            Id(At(J(LocalAppData, "Packages", "CanonicalGroupLimited.Ubuntu22.04LTS_79rhkp1fndgsc", "LocalState", "ext4.vhdx"), true, 1)));
        Assert.Equal("pf.epic-ue", Id(At(J(ProgramFiles, "Epic Games", "UE_5.7"))));
        Assert.NotEqual("pf.epic-ue", Id(At(J(ProgramFiles, "Epic Games", "Launcher"))));
        Assert.Equal("user.onedrive-business", Id(At(J(Profile, "OneDrive - Contoso"))));
        Assert.NotEqual("user.onedrive-business", Id(At(J(Profile, "OneDrive"))));
        Assert.Equal("game.epic.webcache", Id(At(J(LocalAppData, "EpicGamesLauncher", "Saved", "webcache_4147"))));
        Assert.Equal("pd.regid", Id(At(J(ProgramData, "regid.1991-06.com.microsoft"))));

        var p = new PathPattern(@"%LOCALAPPDATA%\*-updater");
        Assert.True(p.IsMatch(J(LocalAppData, "x-updater")));
        Assert.False(p.IsMatch(J(LocalAppData, "x-updater", "sub")));
        Assert.False(p.IsMatch(J(LocalAppData, "x-updater2")));
    }

    // 2. '**' for zero or more segments, also in the middle and inside `under`.
    [Fact]
    public void DoubleStar_InMiddleAndInUnder()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("proj.node-modules-pf", Id(At(J(ProgramFiles, "node_modules"))));
            Assert.Equal("proj.node-modules-pf", Id(At(J(ProgramFiles, "nodejs", "node_modules"))));
            Assert.Equal("proj.node-modules-pf", Id(At(J(ProgramFiles, "App", "resources", "app", "node_modules"))));
            Assert.Equal("dev.docker-wsl-vhdx", Id(At(J(LocalAppData, "Docker", "wsl", "disk", "ext4.vhdx"), true, 1)));
            Assert.Equal("dev.docker-wsl-vhdx", Id(At(J(LocalAppData, "Docker", "wsl", "main", "data", "ext4.vhdx"), true, 1)));
        }

        foreach (var under in new[] { @"**\\node_modules", "**/node_modules" })
        {
            var set = RuleSet.FromJson($$"""{ "rules": [ { "id": "u", "match": { "name": "dist", "under": "{{under}}" }, "title": "t", "badge": "check", "summary": "s" } ] }""");
            Assert.Empty(set.Errors);
            var nm = Dir(Dir(Root(AbsRoot), "proj"), "node_modules");
            Assert.Equal("u", Id(set, Dir(Dir(nm, "pkg"), "dist")));
            Assert.Equal("u", Id(set, Dir(Dir(Dir(Dir(nm, "a"), "node_modules"), "b"), "dist")));
            Assert.Equal("", Id(set, Dir(Dir(Root(AbsRoot), "proj"), "dist")));
        }
    }

    // 3. Globs in `sibling`.
    [Fact]
    public void SiblingGlobs()
    {
        var root = Root(AbsRoot);
        var cs = Dir(root, "App");
        File(cs, "App.csproj", 1);
        Assert.Equal("proj.dotnet-bin-cs", Id(Dir(cs, "bin")));
        Assert.Equal("proj.dotnet-obj-cs", Id(Dir(cs, "obj")));

        var game = Dir(root, "Game");
        File(game, "Game.uproject", 1);
        Assert.Equal("proj.unreal-intermediate", Id(Dir(game, "Intermediate")));

        var sln = Dir(root, "Solution");
        File(sln, "All.sln", 1);
        Assert.Equal("proj.nuget-packages-folder", Id(Dir(sln, "packages")));

        var plain = Dir(root, "Plain");
        File(plain, "readme.txt", 1);
        Assert.NotEqual("proj.dotnet-bin-cs", Id(Dir(plain, "bin")));
    }

    // 4. `extension` and names combined with qualifiers.
    [Fact]
    public void ExtensionAndNameQualifiers()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("win.installer-msi", Id(At(J(WinDir, "Installer", "1a2b3c.msi"), true, 1)));
            Assert.Equal("win.installer-msp", Id(At(J(WinDir, "Installer", "{GUID}", "patch.msp"), true, 1)));
            Assert.NotEqual("win.installer-msi", Id(At(J(Profile, "Downloads", "setup.msi"), true, 1)));
        }

        var root = Root(AbsRoot);
        var proj = Dir(root, "web");
        File(proj, "package.json", 1);
        Assert.Equal("proj.node-modules", Id(Dir(proj, "node_modules")));
        Assert.NotEqual("proj.node-modules", Id(Dir(Dir(root, "stray"), "node_modules")));

        // Python's own standard library has a small Lib\venv package; only a real environment is big.
        var stdlibVenv = Dir(Dir(Dir(root, "Python311"), "Lib"), "venv");
        stdlibVenv.Size = 200_000;
        Assert.NotEqual("proj.venv", Id(stdlibVenv));
        var env = Dir(Dir(root, "tool"), "venv");
        env.Size = 6_000_000;
        Assert.Equal("proj.venv", Id(env));
        var dotEnv = Dir(Dir(root, "tool2"), ".venv");
        dotEnv.Size = 6_000_000;
        Assert.Equal("proj.venv-dot", Id(dotEnv));
    }

    // 5. %LOCALAPPDATA% / %APPDATA% expand to every profile, like %USERPROFILE%.
    [Fact]
    public void AppDataTokens_CoverOtherProfiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var other = J(UsersRoot, "SomeoneElse");
        var localRel = Path.GetRelativePath(Profile, LocalAppData);
        var roamingRel = Path.GetRelativePath(Profile, RoamingAppData);

        Assert.Equal("user.temp", Id(At(J(other, localRel, "Temp"))));
        Assert.Equal("proj.node-modules-npm-global", Id(At(J(other, roamingRel, "npm", "node_modules"))));
        Assert.Equal("user.downloads", Id(At(J(other, "Downloads"))));
    }

    // 6. '~' and %HOME% are the same Unix home token.
    [Fact]
    public void TildeAndHome_AreEquivalent()
    {
        var usersRoot = UsersRoot;
        if (string.IsNullOrEmpty(usersRoot) || usersRoot.TrimEnd('\\', '/').Length <= 2) return;

        var tilde = new PathPattern("~/.cache/pip");
        var home = new PathPattern("%HOME%/.cache/pip");
        foreach (var path in new[] { J(Profile, ".cache", "pip"), J(usersRoot, "other", ".cache", "pip") })
        {
            Assert.True(tilde.IsMatch(path), path);
            Assert.True(home.IsMatch(path), path);
        }
        Assert.False(tilde.IsMatch(J(Profile, ".cache", "pip", "x")));

        // The Linux file uses '~'; compiled here, it must match the profile the same way.
        var linux = RuleLoader.LoadEmbedded().Where(d => d.Id == "linux.pip-cache" || d.Match.Path == "~/.cache/pip")
            .Select(d => { d.Platforms = null; return d; }).ToList();
        Assert.NotEmpty(linux);
        var set = RuleSet.FromDtos(linux);
        Assert.NotNull(set.Explain(At(J(Profile, ".cache", "pip"))));
    }

    // 7. Equal specificity: the higher priority wins, whatever the load order.
    [Fact]
    public void EqualTier_HigherPriorityWins()
    {
        string generic = """{ "id": "generic", "match": { "name": "node_modules", "sibling": "package.json" }, "priority": 60, "title": "t", "badge": "safe", "summary": "s" }""";
        string app = $$"""{ "id": "app", "match": { "name": "node_modules", "under": "{{AbsRoot.Replace("\\", "\\\\")}}/Apps" }, "priority": 100, "title": "t", "badge": "app", "summary": "s" }""";
        var root = Root(AbsRoot);
        var tool = Dir(Dir(root, "Apps"), "tool");
        File(tool, "package.json", 1);
        var nm = Dir(tool, "node_modules");

        Assert.Equal("app", Id(RuleSet.FromJson($$"""{ "rules": [ {{generic}}, {{app}} ] }"""), nm));
        Assert.Equal("app", Id(RuleSet.FromJson($$"""{ "rules": [ {{app}}, {{generic}} ] }"""), nm));

        // Unreal Engine's own plugins are not project build output.
        var plugin = Dir(Dir(Dir(Dir(Root(AbsRoot), "UE_5.7"), "Engine"), "Plugins"), "Niagara");
        File(plugin, "Niagara.uplugin", 1);
        Assert.Equal("proj.unreal-engine-plugin-intermediate", Id(Dir(plugin, "Intermediate")));
        var own = Dir(Dir(Dir(Root(AbsRoot), "MyGame"), "Plugins"), "Tool");
        File(own, "Tool.uplugin", 1);
        Assert.Equal("proj.unreal-plugin-intermediate", Id(Dir(own, "Intermediate")));

        if (!OperatingSystem.IsWindows()) return;
        var pfApp = At(J(ProgramFiles, "nodejs", "node_modules"));
        File(pfApp.Parent!, "package.json", 1);
        Assert.Equal("proj.node-modules-pf", Id(pfApp));
        var global = At(J(RoamingAppData, "npm", "node_modules"));
        File(global.Parent!, "package.json", 1);
        Assert.Equal("proj.node-modules-npm-global", Id(global));
    }

    // Wrong verdicts found by the coverage run on a real drive (tools/DiskLore.Coverage).
    [Fact]
    public void ProjectRules_DoNotFireInsideInstalledPrograms()
    {
        var root = Root(AbsRoot);

        // Blender ships Python whose standard library venv module is over 5 MB.
        var lib = Dir(Dir(Dir(root, "Blender"), "python"), "lib");
        File(lib, "os.py", 1);
        var stdlibVenv = Dir(lib, "venv");
        stdlibVenv.Size = 8_200_000;
        Assert.Equal("proj.venv-stdlib", Id(stdlibVenv));

        var sitePackages = Dir(lib, "site-packages");
        Assert.Equal("proj.egg-info-installed", Id(Dir(sitePackages, "Cython-3.0.11-py3.13.egg-info")));
        Assert.Equal("proj.egg-info", Id(Dir(Dir(root, "mylib"), "mylib.egg-info")));

        // Build output of packages inside node_modules is left to the node_modules rule (and its inheritance).
        var nmProj = Dir(root, "web");
        File(nmProj, "package.json", 1);
        var pkg = Dir(Dir(Dir(nmProj, "node_modules"), "bcrypt"), "build");
        Assert.Null(Kb.Explain(pkg));

        if (!OperatingSystem.IsWindows()) return;
        var liveLink = At(J(ProgramFiles, "Epic Games", "UE_5.7", "Engine", "Source", "Programs", "LiveLinkHub", "Intermediate"));
        File(liveLink.Parent!, "LiveLinkHub.uproject", 1);
        Assert.Equal("pf.epic-ue-intermediate", Id(liveLink));

        var appVenv = At(J(ProgramFiles, "SomeAiTool", "venv"), size: 900_000_000);
        Assert.Equal("proj.venv-in-programs-pf", Id(appVenv));
        var userVenv = At(J(LocalAppData, "Programs", "SomeAiTool", "resources", ".venv"), size: 900_000_000);
        Assert.Equal("proj.venv-in-programs-local-dot", Id(userVenv));
    }

    [Fact]
    public void ParentFolders_OfKnownAppDataFolders_AreExplained()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var name in new[] { "pip", "uv", "zen", "Google", "Mozilla", "Steam", "OpenAI", "b1", "Claude-Data", "DiskLore" })
            Assert.NotEqual("", Id(At(J(LocalAppData, name))));
        foreach (var name in new[] { "Mozilla", "zen", "uv", "nvm", "ModrinthApp" })
            Assert.NotEqual("", Id(At(J(RoamingAppData, name))));
    }
}
