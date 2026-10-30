using System.Runtime.CompilerServices;
using System.Text;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Programs;

/// <summary>
/// Windows only. Flags direct children of AppData\Local, AppData\Roaming, ProgramData and Program Files
/// that match no installed program. It is a guess, so the confidence stays low.
/// </summary>
public sealed class LeftoverHeuristic : IExplainer
{
    private sealed class Memo { public Explanation? Value; }

    private static readonly HashSet<string> AllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Packages", "Temp", "Tmp", "Programs", "Comms", "ConnectedDevicesPlatform",
        "CrashDumps", "D3DSCache", "PeerDistRepub", "Publishers", "VirtualStore", "PlaceholderTileLogoFolder",
        "assembly", "Application Data", "History", "Microsoft Help", "ssh", "USOPrivate", "USOShared",
        "SoftwareDistribution", "Package Cache", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "Realtek",
        "Common Files", "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows NT",
        "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar", "Windows Portable Devices",
        "Windows Security", "Windows Kits", "WindowsApps", "WindowsPowerShell", "PowerShell", "ModifiableWindowsApps",
        "Internet Explorer", "Reference Assemblies", "dotnet", "MSBuild", "IIS", "IIS Express", "Uninstall Information",
        "desktop.ini", "Microsoft.NET", "MicrosoftEdge", "Microsoft OneDrive", "OneDrive", "Diagnostics",
        "DiagnosticsHub", "SystemApps", "SystemCertificates", "Speech", "Speech_OneCore", "WindowsHolographicDevices",
        "Temporary Internet Files", "Desktop", "Documents", "Downloads", "Favorites", "Start Menu", "Templates",
        "Application Compatibility", "Package", "Packages", "regid", "ProgramData", "SoftwareDistribution",
        "Windows Identity Foundation", "Windows Workflow Foundation", "Microsoft Analysis Services",
        "Microsoft SQL Server", "Microsoft SDKs", "Microsoft Shared", "Microsoft Visual Studio", "Microsoft Office",
        "WindowsApps", "Dell", "HP", "Hewlett-Packard", "Lenovo", "ASUS", "Acer", "Apple", "Apple Computer",
        "Sun", "Oracle", "Java", "npm", "npm-cache", "NuGet", "pip", "node_modules", "$Recycle.Bin",
        "System Volume Information", "Recovery", "MiracastView", "Notifications", "GameBar", "AppReadiness",
        "Local", "LocalLow", "Roaming", "AppData",
    };

    private static readonly string[] AllowPrefixes =
    {
        "regid.", "Windows ", "Microsoft", "Windows.", "MicrosoftEdge", "{", "$", "NVIDIA ", "Intel ", "AMD ",
    };

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "corp", "corporation", "ltd", "limited", "llc", "gmbh", "co", "company", "software",
        "technologies", "technology", "games", "game", "studio", "studios", "the", "x64", "x86", "64bit", "32bit",
        "edition", "pty", "sa", "ag", "bv", "oy",
    };

    private static readonly StringComparison PathCmp =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly bool _enabled;
    private readonly Func<DateTime> _utcNow;
    private readonly HashSet<string> _containers;           // exact container paths, lower-cased, no trailing separator
    private readonly string? _usersRoot;                    // parent of the user profile folder
    private readonly List<string> _names = new();           // normalized program names and publishers, len >= 3
    private readonly List<string> _locations = new();       // install locations with trailing separator
    private readonly List<string> _families = new();        // store family names (lower-case)
    private readonly ConditionalWeakTable<ScanNode, Memo> _memo = new();

    public LeftoverHeuristic(IReadOnlyList<InstalledProgram> programs)
        : this(programs, DefaultContainers(), UsersRoot(), OperatingSystem.IsWindows(), () => DateTime.UtcNow) { }

    // Test seam: explicit container folders, platform switch and clock.
    internal LeftoverHeuristic(IReadOnlyList<InstalledProgram> programs, IEnumerable<string> containers,
        string? usersRoot, bool enabled, Func<DateTime> utcNow)
    {
        _enabled = enabled;
        _utcNow = utcNow;
        _usersRoot = string.IsNullOrEmpty(usersRoot) ? null : usersRoot.TrimEnd('\\', '/');
        _containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in containers)
            if (!string.IsNullOrEmpty(c)) _containers.Add(c.TrimEnd('\\', '/'));

        foreach (var p in programs)
        {
            AddName(Normalize(p.DisplayName));
            if (p.Publisher != null) AddName(Normalize(p.Publisher));

            if (!string.IsNullOrWhiteSpace(p.InstallLocation))
            {
                var loc = p.InstallLocation.Trim().Trim('"').TrimEnd('\\', '/');
                if (loc.Length > 3) _locations.Add(loc + Path.DirectorySeparatorChar);
            }

            if (p.IsStoreApp && !string.IsNullOrEmpty(p.PackageFamilyName))
                _families.Add(p.PackageFamilyName.ToLowerInvariant());
        }
    }

    private void AddName(string n)
    {
        if (n.Length >= 3) _names.Add(n);
    }

    private static IEnumerable<string> DefaultContainers() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    };

    private static string? UsersRoot()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? null : Path.GetDirectoryName(profile.TrimEnd('\\', '/'));
    }

    public Explanation? Explain(ScanNode node)
    {
        if (!_enabled) return null;
        if (!node.IsDirectory || node.IsReparsePoint || node.Parent == null) return null;

        // Cheap pre-check before any path building: the parent must look like a container.
        var parentName = node.Parent.Parent == null ? Last(node.Parent.Name) : node.Parent.Name;
        if (!LooksLikeContainer(parentName)) return null;

        if (_memo.TryGetValue(node, out var cached)) return cached.Value;
        var result = Compute(node);
        _memo.Add(node, new Memo { Value = result });
        return result;
    }

    private static bool LooksLikeContainer(string name) =>
        name.Equals("Local", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Roaming", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Program Files", StringComparison.OrdinalIgnoreCase);

    private static string Last(string path)
    {
        var t = path.TrimEnd('\\', '/');
        int i = t.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? t : t.Substring(i + 1);
    }

    private Explanation? Compute(ScanNode node)
    {
        var parentPath = node.Parent!.FullPath.TrimEnd('\\', '/');
        if (!IsContainer(parentPath)) return null;

        var name = node.Name;
        if (IsAllowListed(name)) return null;

        var token = Normalize(name);
        if (token.Length < 3) return null;   // too short to compare safely: stay quiet

        if (MatchesProgram(token, name, parentPath + Path.DirectorySeparatorChar + name)) return null;

        bool recent = (_utcNow() - node.LastWriteUtc) < TimeSpan.FromDays(30);
        double confidence = recent ? 0.4 : 0.55;

        return new Explanation(
            $"Possible leftover: {name}",
            Badge.Leftover,
            null,
            "No installed program matches this folder",
            "This folder sits where programs keep their data, but no installed program matches its name. " +
            "It may be left behind by a program that was uninstalled. This is a heuristic guess based on names.",
            "Programs often leave settings, caches and logs behind when they are uninstalled.",
            "If the folder belongs to a portable app, a game launcher, a driver or a Windows component, " +
            "deleting it can reset or break that software. Check what created it before deleting.",
            "Search the folder name online, or look inside for a program name, before removing it.",
            null,
            ExplanationSource.Heuristic,
            confidence,
            null);
    }

    private bool IsContainer(string parentPath)
    {
        if (_containers.Contains(parentPath)) return true;
        if (_usersRoot == null) return false;

        // <users root>\<name>\AppData\Local|Roaming for every profile on the machine.
        if (!parentPath.StartsWith(_usersRoot + Path.DirectorySeparatorChar, PathCmp)) return false;
        var rest = parentPath.Substring(_usersRoot.Length + 1).Replace('/', '\\').Split('\\');
        return rest.Length == 3 &&
               rest[1].Equals("AppData", StringComparison.OrdinalIgnoreCase) &&
               (rest[2].Equals("Local", StringComparison.OrdinalIgnoreCase) ||
                rest[2].Equals("Roaming", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowListed(string name)
    {
        if (AllowList.Contains(name)) return true;
        foreach (var prefix in AllowPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool MatchesProgram(string token, string rawName, string folderPath)
    {
        foreach (var n in _names)
            if (n.Contains(token, StringComparison.Ordinal) || token.Contains(n, StringComparison.Ordinal))
                return true;

        var folderWithSep = folderPath + Path.DirectorySeparatorChar;
        foreach (var loc in _locations)
            if (loc.StartsWith(folderWithSep, PathCmp)) return true;

        var lower = rawName.ToLowerInvariant();
        foreach (var f in _families)
            if (f.StartsWith(lower, StringComparison.Ordinal) || Normalize(f).Contains(token, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>
    /// Lower-case, without version numbers, company suffixes, spaces and punctuation:
    /// "Unity Technologies Inc." becomes "unity".
    /// </summary>
    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var word = new StringBuilder();

        void Flush()
        {
            if (word.Length == 0) return;
            var w = word.ToString();
            word.Clear();
            if (NoiseWords.Contains(w)) return;
            if (IsVersionToken(w)) return;
            sb.Append(w);
        }

        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) word.Append(char.ToLowerInvariant(ch));
            else Flush();
        }
        Flush();
        return sb.ToString();
    }

    private static bool IsVersionToken(string w)
    {
        // "6", "2019", "v2", "x64" style tokens.
        int start = (w[0] == 'v' && w.Length > 1) ? 1 : 0;
        for (int i = start; i < w.Length; i++)
            if (!char.IsDigit(w[i])) return false;
        return true;
    }
}
