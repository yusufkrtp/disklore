using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Programs;
using Xunit;

namespace DiskLore.Tests.Programs;

public class ProgramTests
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
    [InlineData("Unity")]        // display name contains folder
    [InlineData("UnityTechnologies")] // publisher
    [InlineData("Unity Hub")]    // folder contains display token
    public void Leftover_Matches_By_Name_Or_Publisher(string folder)
    {
        var h = Heuristic(new[] { Prog("Unity Hub 3.8", "Unity Technologies", null) });
        Assert.Null(h.Explain(Folder(folder)));
    }

    [Fact]
    public void Leftover_Matches_By_Install_Location()
    {
        var loc = Path.Combine(Drive, "Users", "me", "AppData", "Local", "Zzcustom", "bin");
        var h = Heuristic(new[] { Prog("Totally Different Name", "Nobody", loc) });
        Assert.Null(h.Explain(Folder("Zzcustom")));
    }

    [Fact]
    public void Leftover_Matches_By_Store_Family()
    {
        var h = Heuristic(new[] { Store("Some App", "Contoso.CoolPhotos_abc123") });
        Assert.Null(h.Explain(Folder("Contoso.CoolPhotos")));
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
