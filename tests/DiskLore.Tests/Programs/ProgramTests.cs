using DiskLore.Core.Explanations;
using DiskLore.Core.Ai;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Rules;
using DiskLore.Core.Scanning;
using DiskLore.Core.Programs;
using Xunit;

namespace DiskLore.Tests.Programs;

public class ProgramTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly string Drive = OperatingSystem.IsWindows() ? @"C:\" : "/";

    // Builds the chain of directories for the given segments under a drive root and returns the last node.
    private static ScanNode Chain(params string[] segments)
    {
        var node = new ScanNode(Drive, null, true);
        foreach (var s in segments)
        {
            var child = new ScanNode(s, null, true);
            node.AddChild(child);
            node = child;
        }
        return node;
    }

    private static ScanNode Child(ScanNode parent, string name, bool dir = true)
    {
        var c = new ScanNode(name, null, dir);
        parent.AddChild(c);
        return c;
    }

    private static InstalledProgram Prog(string name, string? publisher, string? location, string? version = "1.0") =>
        new(name, publisher, location, version, null, false, null);

    private static InstalledProgram Store(string name, string family, string? location = null) =>
        new(name, null, location, "1.0", null, true, family);

    // ---------- ProgramMatcher ----------

    [Fact]
    public void Matcher_InstallRoot_And_Descendant_Match_Ancestor_Does_Not()
    {
        var loc = Path.Combine(Drive, "Program Files", "Unity");
        var matcher = new ProgramMatcher(new[] { Prog("Unity 6.3", "Unity Technologies", loc, "6.3.0") });

        var pf = Chain("Program Files");
        var root = Child(pf, "Unity");
        var editor = Child(root, "Editor");
        var data = Child(editor, "Data");

        Assert.Null(matcher.FindOwner(pf));
        Assert.NotNull(matcher.FindOwner(root));
        Assert.Equal("Unity 6.3", matcher.FindOwner(editor)?.DisplayName);
        Assert.Equal("Unity 6.3", matcher.FindOwner(data)?.DisplayName);

        Assert.Null(matcher.Explain(pf));
        var e = matcher.Explain(root)!;
        Assert.Equal(Badge.App, e.Badge);
        Assert.Equal(ExplanationSource.InstalledProgram, e.Source);
        Assert.Equal("Unity 6.3", e.Title);
        Assert.Equal("Unity Technologies", e.Owner);
        Assert.Equal("Installed program: Unity 6.3 (Unity Technologies)", e.Summary);
        Assert.Contains("6.3.0", e.WhatItIs);
        Assert.Equal("Uninstall it from Settings > Apps", e.SaferAlternative);
        Assert.Equal("settingsUri", e.Action!.Kind);
        Assert.Equal("ms-settings:appsfeatures", e.Action.Value);

        var d = matcher.Explain(editor)!;
        Assert.Equal(Badge.App, d.Badge);
    }

    [Fact]
    public void Matcher_Sibling_With_Common_Prefix_Does_Not_Match()
    {
        var loc = Path.Combine(Drive, "Program Files", "Unity");
        var matcher = new ProgramMatcher(new[] { Prog("Unity", null, loc) });
        var pf = Chain("Program Files");
        Assert.Null(matcher.FindOwner(Child(pf, "Unity Hub")));
        Assert.Null(matcher.FindOwner(Child(pf, "UnityX")));
    }

    [Fact]
    public void Matcher_Trailing_Separator_And_Quotes_In_Location_Are_Tolerated()
    {
        var loc = "\"" + Path.Combine(Drive, "Apps", "Foo") + Path.DirectorySeparatorChar + "\"";
        var matcher = new ProgramMatcher(new[] { Prog("Foo", null, loc) });
        var apps = Chain("Apps");
        Assert.NotNull(matcher.FindOwner(Child(apps, "Foo")));
    }

    [Fact]
    public void Matcher_Deepest_Install_Root_Wins()
    {
        var outer = Path.Combine(Drive, "Suite");
        var inner = Path.Combine(Drive, "Suite", "Plugin");
        var matcher = new ProgramMatcher(new[] { Prog("Suite", null, outer), Prog("Plugin", null, inner) });
        var suite = Chain("Suite");
        var plugin = Child(suite, "Plugin");
        var file = Child(plugin, "a.dll", false);
        Assert.Equal("Suite", matcher.FindOwner(suite)?.DisplayName);
        Assert.Equal("Plugin", matcher.FindOwner(plugin)?.DisplayName);
        Assert.Equal("Plugin", matcher.FindOwner(file)?.DisplayName);
    }

    [Fact]
    public void Matcher_Store_Package_Folders()
    {
        var term = Store("Windows Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe");
        var matcher = new ProgramMatcher(new[] { term });

        var apps = Chain("Program Files", "WindowsApps");
        var pkg = Child(apps, "Microsoft.WindowsTerminal_1.21.2361.0_x64__8wekyb3d8bbwe");
        var inner = Child(pkg, "WindowsTerminal.exe", false);
        var other = Child(apps, "Microsoft.Other_1.0.0.0_x64__8wekyb3d8bbwe");

        Assert.Equal("Windows Terminal", matcher.FindOwner(pkg)?.DisplayName);
        Assert.Equal("Windows Terminal", matcher.FindOwner(inner)?.DisplayName);
        Assert.Null(matcher.FindOwner(other));

        var packages = Chain("Users", "me", "AppData", "Local", "Packages");
        var family = Child(packages, "Microsoft.WindowsTerminal_8wekyb3d8bbwe");
        var lower = Child(packages, "microsoft.windowsterminal_8wekyb3d8bbwe");
        Assert.NotNull(matcher.FindOwner(family));
        Assert.NotNull(matcher.FindOwner(lower));   // case-insensitive
        Assert.Equal(Badge.App, matcher.Explain(family)!.Badge);
    }

    [Fact]
    public void Matcher_Case_Insensitive_On_Windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var matcher = new ProgramMatcher(new[] { Prog("Foo", null, @"C:\PROGRAM FILES\FOO") });
        var pf = Chain("Program Files");
        Assert.NotNull(matcher.FindOwner(Child(pf, "Foo")));
    }

    [Fact]
    public void Matcher_Drive_Root_Location_Is_Ignored()
    {
        var matcher = new ProgramMatcher(new[] { Prog("Weird", null, Drive) });
        Assert.Null(matcher.FindOwner(Chain("Anything")));
    }

    [Fact]
    [Trait("Category", "Manual")]
    public async Task Manual_ProgramFiles_Unknowns()
    {
        if (Environment.GetEnvironmentVariable("DISKLORE_MANUAL") != "1") return;
        var programs = PlatformServices.Create().Programs.GetInstalled();
        var service = new ExplanationService(RuleSet.Load(null), new ProgramMatcher(programs), new LeftoverHeuristic(programs),
            new AiCache(Path.Combine(Path.GetTempPath(), "disklore-pf-" + Guid.NewGuid().ToString("N") + ".json")));
        foreach (var root in new[] { @"C:\Program Files", @"C:\Program Files (x86)" })
        {
            var scan = await new Scanner().ScanAsync(root, null, CancellationToken.None);
            var unknown = scan.Root.Children.Where(c => c.IsDirectory && service.Explain(c).Badge == Badge.Unknown).ToList();
            var msg = $"UNKNOWN under {root}: {unknown.Count} of {scan.Root.Children.Count(c => c.IsDirectory)}";
            output.WriteLine(msg); Console.Error.WriteLine(msg);
            foreach (var c in unknown.OrderByDescending(c => c.Size))
            {
                var line = $"   {c.Size / 1048576.0,10:F1} MB  {c.Name}";
                output.WriteLine(line); Console.Error.WriteLine(line);
            }
        }
    }

    private static string PfRoot => Path.Combine(Drive, "Program Files");

    [Fact]
    public void Matcher_VendorFolder_With_One_Program()
    {
        var loc = Path.Combine(PfRoot, "Google", "Chrome", "Application");
        var matcher = new ProgramMatcher(new[] { Prog("Google Chrome", "Google LLC", loc, "130.0") }, new[] { PfRoot });
        var pf = Chain("Program Files");
        var google = Child(pf, "Google");
        var e = matcher.Explain(google)!;
        Assert.Equal(Badge.App, e.Badge);
        Assert.Equal(ExplanationSource.InstalledProgram, e.Source);
        Assert.Equal(1.0, e.Confidence);
        Assert.Equal("Google", e.Title);
        Assert.Equal("Google LLC", e.Owner);
        Assert.Equal("Contains Google Chrome", e.Summary);
        Assert.Contains("130.0", e.WhatItIs);
        Assert.Equal("ms-settings:appsfeatures", e.Action!.Value);
        Assert.Null(matcher.FindOwner(google));

        // Deeper nodes follow the install-root logic, siblings and non-ancestors stay unmatched.
        Assert.Equal("Google Chrome", matcher.Explain(Child(Child(google, "Chrome"), "Application"))!.Title);
        Assert.Null(matcher.Explain(Child(google, "Chrome")));
        Assert.Null(matcher.Explain(Child(pf, "Other")));
    }

    [Fact]
    public void Matcher_VendorFolder_With_Several_Programs()
    {
        var progs = new[]
        {
            Prog("Tool A", "Acme", Path.Combine(PfRoot, "Acme", "A")),
            Prog("Tool B", "Acme", Path.Combine(PfRoot, "Acme", "B")),
            Prog("Tool C", "Other", Path.Combine(PfRoot, "Acme", "C")),
        };
        var matcher = new ProgramMatcher(progs, new[] { PfRoot });
        var e = matcher.Explain(Child(Chain("Program Files"), "Acme"))!;
        Assert.Equal("Acme", e.Owner);
        Assert.Equal("Contains 3 installed programs: Tool A, Tool B, Tool C", e.Summary);
    }

    [Fact]
    public void Matcher_VendorFolder_Summary_Is_Trimmed()
    {
        var progs = Enumerable.Range(1, 8).Select(i =>
            Prog("A Rather Long Program Name " + i, null, Path.Combine(PfRoot, "Big", "p" + i))).ToArray();
        var e = new ProgramMatcher(progs, new[] { PfRoot }).Explain(Child(Chain("Program Files"), "Big"))!;
        Assert.True(e.Summary.Length <= 90);
        Assert.Equal("Big", e.Owner);
    }

    [Fact]
    public void Matcher_VendorFolder_Only_Directly_Under_Program_Files()
    {
        var matcher = new ProgramMatcher(new[] { Prog("X", null, Path.Combine(PfRoot, "Acme", "X")) }, new[] { PfRoot });
        Assert.Null(matcher.Explain(Child(Chain("Elsewhere"), "Acme")));
        Assert.Null(matcher.Explain(Child(Chain("Program Files", "Sub"), "Acme")));
    }

    // ---------- LeftoverHeuristic ----------

    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static LeftoverHeuristic Heuristic(IReadOnlyList<InstalledProgram> programs, bool enabled = true) =>
        new(programs, new[] { Path.Combine(Drive, "Users", "me", "AppData", "Local") }, Path.Combine(Drive, "Users"), enabled, () => Now);

    private static ScanNode LocalAppData() => Chain("Users", "me", "AppData", "Local");

    private static ScanNode Folder(string name, int ageDays = 200)
    {
        var f = Child(LocalAppData(), name);
        f.LastWriteUtc = Now.AddDays(-ageDays);
        return f;
    }

    [Fact]
    public void Leftover_Unmatched_Folder_Is_Flagged()
    {
        var h = Heuristic(new[] { Prog("Unity Hub", "Unity Technologies", null) });
        var e = h.Explain(Folder("ObscureOldTool"))!;
        Assert.Equal(Badge.Leftover, e.Badge);
        Assert.Equal(ExplanationSource.Heuristic, e.Source);
        Assert.Equal(0.55, e.Confidence);
        Assert.Equal("Possible leftover: ObscureOldTool", e.Title);
        Assert.Equal("No installed program matches this folder", e.Summary);
    }

    [Fact]
    public void Leftover_Recent_Folder_Has_Lower_Confidence()
    {
        var h = Heuristic(Array.Empty<InstalledProgram>());
        Assert.Equal(0.4, h.Explain(Folder("FreshTool", 3))!.Confidence);
    }

    [Theory]
    [InlineData("Unity")]             // display name starts with the folder name
    [InlineData("Unity Technologies")] // publisher-style name, company suffix ignored
    [InlineData("Unity Hub")]         // same name
    public void NameMatch_InAppData_IsAttributedToTheProgram(string folder)
    {
        var h = Heuristic(new[] { Prog("Unity Hub 3.8", "Unity Technologies", null) });
        var e = h.Explain(Folder(folder))!;
        Assert.Equal(Badge.App, e.Badge);
        Assert.Equal(ExplanationSource.InstalledProgram, e.Source);
        Assert.Equal(0.6, e.Confidence);
        Assert.Equal("Data of Unity Hub 3.8", e.Title);
        Assert.Equal("Unity Technologies", e.Owner);
        Assert.Equal("Settings and data of Unity Hub 3.8 (installed)", e.Summary);
        Assert.StartsWith("Unity Hub 3.8 loses its settings, sign-ins or cached data", e.IfDeleted);
        Assert.Equal("Use Unity Hub 3.8's own settings to clear its cache, or uninstall it from Settings > Apps", e.SaferAlternative!.TrimEnd('.'));
        Assert.Equal(new SaferAction("settingsUri", "Open Installed apps", "ms-settings:appsfeatures"), e.Action);
    }

    [Fact]
    public void WeakNameMatch_StaysQuiet()
    {
        // "Runtime" only appears inside the display name: not enough to name the program, not a leftover either.
        var h = Heuristic(new[] { Prog("Microsoft Edge WebView2 Runtime", "Microsoft Corporation", null) });
        Assert.Null(h.Explain(Folder("Runtime")));

        // A short prefix of a much longer name is not enough either.
        var n = Heuristic(new[] { Prog("Narrator Extension - Outlook Web App", "Contoso", null) });
        Assert.Null(n.Explain(Folder("Narrator")));
    }

    [Fact]
    public void DisplayName_BeatsPublisher()
    {
        var h = Heuristic(new[] { Prog("Acme Studio", "Steam", null), Prog("Steam", "Valve Corporation", null) });
        Assert.Equal("Data of Steam", h.Explain(Folder("Steam"))!.Title);

        var byPublisher = Heuristic(new[] { Prog("Hub Tool", "Zorblax Software Ltd", null) });
        Assert.Equal("Data of Hub Tool", byPublisher.Explain(Folder("Zorblax"))!.Title);
    }

    [Fact]
    public void NameMatch_InProgramData_IsAttributed_InProgramFiles_StaysQuiet()
    {
        var pd = Path.Combine(Drive, "ProgramData");
        var pf = Path.Combine(Drive, "Program Files");
        var h = new LeftoverHeuristic(new[] { Prog("Steam", "Valve Corporation", null) }, new[] { pd, pf },
            Path.Combine(Drive, "Users"), true, () => Now);
        Assert.Equal(Badge.App, h.Explain(Child(Chain("ProgramData"), "Steam"))!.Badge);
        var files = h.Explain(Child(Chain("Program Files"), "Steam"))!;
        Assert.Equal(Badge.App, files.Badge);
        Assert.Equal("Files of Steam", files.Title);
        Assert.Equal(Badge.Leftover, h.Explain(Child(Chain("Program Files"), "ObscureOldTool"))!.Badge);
    }

    [Fact]
    public void ProgramFiles_NameMatch_Is_App_WeakMatch_Is_Null()
    {
        var pf = Path.Combine(Drive, "Program Files");
        var progs = new[]
        {
            Prog("OBS Studio", "OBS Project", null),
            Prog("Microsoft Edge WebView2 Runtime", "Microsoft Corporation", null),
        };
        var h = new LeftoverHeuristic(progs, new[] { pf }, Path.Combine(Drive, "Users"), true, () => Now);
        var e = h.Explain(Child(Chain("Program Files"), "obs-studio"))!;
        Assert.Equal(Badge.App, e.Badge);
        Assert.Equal(ExplanationSource.InstalledProgram, e.Source);
        Assert.Equal(0.6, e.Confidence);
        Assert.Equal("Files of OBS Studio", e.Title);
        Assert.Equal("Program files of OBS Studio (installed)", e.Summary);
        Assert.Contains("Settings > Apps", e.SaferAlternative);
        Assert.Null(h.Explain(Child(Chain("Program Files"), "Runtime")));
    }

    [Fact]
    public void Leftover_Matches_By_Install_Location()
    {
        var loc = Path.Combine(Drive, "Users", "me", "AppData", "Local", "Zzcustom", "bin");
        var h = Heuristic(new[] { Prog("Totally Different Name", "Nobody", loc) });
        Assert.Equal("Data of Totally Different Name", h.Explain(Folder("Zzcustom"))!.Title);
    }

    [Fact]
    public void Leftover_Matches_By_Store_Family()
    {
        var h = Heuristic(new[] { Store("Some App", "Contoso.CoolPhotos_abc123") });
        Assert.Equal("Data of Some App", h.Explain(Folder("Contoso.CoolPhotos"))!.Title);
    }

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("Temp")]
    [InlineData("Packages")]
    [InlineData("NVIDIA")]
    [InlineData("regid.1991-06.com.microsoft")]
    [InlineData("Windows Defender Advanced Threat Protection")]
    [InlineData("D3DSCache")]
    [InlineData("WindowsApps")]
    [InlineData("dotnet")]
    public void Leftover_AllowList_Is_Never_Flagged(string folder)
    {
        var h = Heuristic(Array.Empty<InstalledProgram>());
        Assert.Null(h.Explain(Folder(folder)));
    }

    [Fact]
    public void Leftover_Only_For_Container_Children()
    {
        var h = Heuristic(Array.Empty<InstalledProgram>());
        var elsewhere = Child(Chain("Users", "me", "Documents"), "ObscureOldTool");
        Assert.Null(h.Explain(elsewhere));
        var deeper = Child(Folder("SomeVendor"), "ObscureOldTool");
        Assert.Null(h.Explain(deeper));
        var file = Child(LocalAppData(), "ObscureOldTool.dat", false);
        Assert.Null(h.Explain(file));
    }

    [Fact]
    public void Leftover_Other_User_Profiles_Count()
    {
        var h = Heuristic(Array.Empty<InstalledProgram>());
        var other = Child(Chain("Users", "someoneelse", "AppData", "Roaming"), "ObscureOldTool");
        other.LastWriteUtc = Now.AddDays(-100);
        Assert.NotNull(h.Explain(other));
    }

    [Fact]
    public void Leftover_Disabled_Returns_Null()
    {
        var h = Heuristic(Array.Empty<InstalledProgram>(), enabled: false);
        Assert.Null(h.Explain(Folder("ObscureOldTool")));
    }

    [Fact]
    public void Leftover_Public_Constructor_Returns_Null_Off_Windows()
    {
        if (OperatingSystem.IsWindows()) return;
        var h = new LeftoverHeuristic(Array.Empty<InstalledProgram>());
        Assert.Null(h.Explain(Folder("ObscureOldTool")));
    }

    [Theory]
    [InlineData("Unity Technologies Inc.", "unity")]
    [InlineData("Unity 6.3", "unity")]
    [InlineData("7-Zip", "zip")]
    [InlineData("Epic Games", "epic")]
    [InlineData("Foo Studio v2", "foo")]
    public void Normalize_Strips_Noise(string input, string expected) =>
        Assert.Equal(expected, LeftoverHeuristic.Normalize(input));
}
