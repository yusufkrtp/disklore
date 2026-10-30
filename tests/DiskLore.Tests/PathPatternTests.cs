using DiskLore.Core.Rules;
using Xunit;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

public class PathPatternTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string UsersRoot => Path.GetDirectoryName(Profile.TrimEnd('\\', '/'))!;
    private static string Sep => Path.DirectorySeparatorChar.ToString();
    private static string P(params string[] parts) => string.Join(Sep, parts);

    [Fact]
    public void WindowsTokens_ExpandAndAreCaseInsensitive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var p = new PathPattern(@"%WINDIR%\WinSxS");
        Assert.True(p.IsMatch(win + @"\WinSxS"));
        Assert.True(p.IsMatch(win.ToUpperInvariant() + @"\winsxs"));
        Assert.False(p.IsMatch(win + @"\WinSxS\Backup"));
        Assert.False(p.IsMatch(win));

        var drive = Path.GetPathRoot(win)!.TrimEnd('\\');
        Assert.True(new PathPattern(@"%SYSTEMDRIVE%\Users").IsMatch(drive + @"\Users"));
        Assert.True(new PathPattern("%PROGRAMDATA%").IsMatch(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
        Assert.True(new PathPattern("%PROGRAMFILES%\\*").IsMatch(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Anything"));
        Assert.True(new PathPattern("%TEMP%").IsMatch(Path.GetTempPath().TrimEnd('\\')));
    }

    [Fact]
    public void ForwardAndBackSlashesAreEquivalent()
    {
        var p = new PathPattern("/data/cache");
        Assert.True(p.IsMatch("/data/cache"));
        Assert.True(p.IsMatch(@"\data\cache"));
        Assert.False(p.IsMatch("/data/cache/x"));
    }

    [Fact]
    public void Star_MatchesExactlyOneSegment()
    {
        var p = new PathPattern("/data/*/cache");
        Assert.True(p.IsMatch("/data/a/cache"));
        Assert.False(p.IsMatch("/data/cache"));
        Assert.False(p.IsMatch("/data/a/b/cache"));
    }

    [Fact]
    public void DoubleStar_MatchesZeroOrMoreSegments()
    {
        var p = new PathPattern("/data/**/cache");
        Assert.True(p.IsMatch("/data/cache"));
        Assert.True(p.IsMatch("/data/a/cache"));
        Assert.True(p.IsMatch("/data/a/b/c/cache"));
        Assert.False(p.IsMatch("/data/a/b/c"));
        Assert.False(p.IsMatch("/other/cache"));

        var tail = new PathPattern("/data/**");
        Assert.True(tail.IsMatch("/data"));
        Assert.True(tail.IsMatch("/data/x/y"));
    }

    [Fact]
    public void StarInsideSegment()
    {
        var p = new PathPattern("/pkgs/*CanonicalGroup*/data");
        Assert.True(p.IsMatch("/pkgs/CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc/data"));
        Assert.True(p.IsMatch("/pkgs/xCanonicalGroup/data"));
        Assert.False(p.IsMatch("/pkgs/Canonical/data"));

        var ext = new PathPattern("/a/Cache_*");
        Assert.True(ext.IsMatch("/a/Cache_001"));
        Assert.False(ext.IsMatch("/a/MyCache_001"));
    }

    [Fact]
    public void CaseRules_FollowTheOperatingSystem()
    {
        var p = new PathPattern("/Data/Cache");
        Assert.Equal(!OperatingSystem.IsLinux(), p.IsMatch("/data/cache"));
        Assert.True(p.IsMatch("/Data/Cache"));
    }

    [Fact]
    public void RelativePatternMatchesAnywhere()
    {
        var p = new PathPattern("node_modules/.cache");
        Assert.True(p.IsMatch("/proj/app/node_modules/.cache"));
        Assert.False(p.IsMatch("/proj/app/node_modules/.cache/x"));
    }

    [Fact]
    public void UserProfileToken_MatchesOwnAndOtherProfiles()
    {
        if (string.IsNullOrEmpty(Profile)) return;
        var usersRoot = UsersRoot;
        if (string.IsNullOrEmpty(usersRoot) || usersRoot.TrimEnd('\\', '/').Length <= 2) return; // e.g. /root

        var p = new PathPattern("%USERPROFILE%/Downloads");
        Assert.True(p.IsMatch(P(Profile, "Downloads")));
        Assert.True(p.IsMatch(P(usersRoot, "SomeoneElse", "Downloads")));
        Assert.False(p.IsMatch(P(usersRoot, "SomeoneElse", "Documents")));
        Assert.False(p.IsMatch(P(usersRoot, "SomeoneElse", "x", "Downloads")));

        var tilde = new PathPattern("~/.cache");
        Assert.True(tilde.IsMatch(P(Profile, ".cache")));
        Assert.True(tilde.IsMatch(P(usersRoot, "other", ".cache")));

        var home = new PathPattern("%HOME%");
        Assert.True(home.IsMatch(Profile));
        Assert.True(home.IsMatch(P(usersRoot, "other")));
    }

    [Fact]
    public void LocalAppData_MatchesOtherProfiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = new PathPattern(@"%LOCALAPPDATA%\Temp");
        Assert.True(p.IsMatch(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp"));
        Assert.True(p.IsMatch(UsersRoot + @"\Other\AppData\Local\Temp"));
        Assert.True(new PathPattern(@"%APPDATA%\Code").IsMatch(UsersRoot + @"\Other\AppData\Roaming\Code"));
    }

    [Fact]
    public void MatchesNodeChain_IncludingMultiSegmentRoot()
    {
        var root = Root(P(OperatingSystem.IsWindows() ? "C:" : "", "Users", "x", "proj"));
        var node = File(Dir(Dir(root, "a"), "b"), "c.bin", 1);

        var abs = new PathPattern(P(OperatingSystem.IsWindows() ? "C:" : "", "Users", "x", "proj", "*", "b", "c.bin"));
        Assert.True(abs.IsMatch(node));
        Assert.True(new PathPattern("**/b/*.bin").IsMatch(node));
        Assert.False(new PathPattern("**/a/*.bin").IsMatch(node));
    }

    [Fact]
    public void UnknownTokenThrows()
    {
        Assert.Throws<ArgumentException>(() => new PathPattern("%NOPE%/x"));
    }
}
