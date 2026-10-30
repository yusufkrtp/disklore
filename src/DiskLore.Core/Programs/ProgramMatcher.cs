using System.Runtime.CompilerServices;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Programs;

public sealed class ProgramMatcher : IExplainer
{
    private sealed class OwnerBox
    {
        public InstalledProgram? Program;
        public bool IsRoot;   // the node is the install root itself, not a descendant
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, InstalledProgram> _byLocation;
    private readonly HashSet<string> _rootNames;                          // last segment of every install location
    private readonly Dictionary<string, InstalledProgram> _storeByFamily;
    private readonly Dictionary<string, InstalledProgram> _storeByPackageKey;   // "<name>_<publisherId>"
    private readonly ConditionalWeakTable<ScanNode, OwnerBox> _memo = new();
    private readonly Dictionary<string, List<InstalledProgram>> _vendorFolders;   // Program Files child path -> programs inside it
    private readonly HashSet<string> _vendorNames;                                // last segment of every key above

    public ProgramMatcher(IReadOnlyList<InstalledProgram> programs)
        : this(programs, DefaultProgramFilesRoots()) { }

    private static IEnumerable<string> DefaultProgramFilesRoots() => new[]
    {
        Environment.GetEnvironmentVariable("ProgramFiles"),
        Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
        Environment.GetEnvironmentVariable("ProgramW6432"),
    }.Where(s => !string.IsNullOrWhiteSpace(s))!;

    // Test seam: explicit Program Files roots.
    internal ProgramMatcher(IReadOnlyList<InstalledProgram> programs, IEnumerable<string?> programFilesRoots)
    {
        _vendorFolders = new Dictionary<string, List<InstalledProgram>>(PathComparer);
        _vendorNames = new HashSet<string>(PathComparer);
        var roots = programFilesRoots
            .Select(r => r?.Trim().Trim('"').TrimEnd('\\', '/'))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(PathComparer)
            .ToList();
        _byLocation = new Dictionary<string, InstalledProgram>(PathComparer);
        _rootNames = new HashSet<string>(PathComparer);
        _storeByFamily = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        _storeByPackageKey = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in programs)
        {
            var loc = NormalizeLocation(p.InstallLocation);
            if (loc != null && _byLocation.TryAdd(loc, p))
                _rootNames.Add(LastSegment(loc));

            if (p.IsStoreApp && !string.IsNullOrEmpty(p.PackageFamilyName))
            {
                _storeByFamily.TryAdd(p.PackageFamilyName, p);
                _storeByPackageKey.TryAdd(p.PackageFamilyName, p);
            }

            if (loc == null) continue;
            foreach (var root in roots)
            {
                if (!loc.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) &&
                    !loc.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var segs = loc.Substring(root!.Length + 1).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length < 2) break;   // the child itself is the install root: handled by MatchSelf
                var key = root + Path.DirectorySeparatorChar + segs[0];
                if (!_vendorFolders.TryGetValue(key, out var list)) _vendorFolders[key] = list = new List<InstalledProgram>();
                list.Add(p);
                _vendorNames.Add(segs[0]);
                break;
            }
        }
    }

    private static string? NormalizeLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var s = location.Trim().Trim('"');
        if (s.Length == 0) return null;
        var trimmed = s.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return null;                          // "/" or "\"
        if (trimmed.Length == 2 && trimmed[1] == ':') return null;     // a bare drive root would own the whole drive
        return trimmed;
    }

    private static string LastSegment(string path)
    {
        var t = path.TrimEnd('\\', '/');
        int i = t.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? t : t.Substring(i + 1);
    }

    public InstalledProgram? FindOwner(ScanNode node) => Resolve(node).Program;

    private OwnerBox Resolve(ScanNode node)
    {
        if (_memo.TryGetValue(node, out var cached)) return cached;

        var box = new OwnerBox();
        var parent = node.Parent;
        var parentBox = parent != null ? Resolve(parent) : null;

        // Deepest install root wins: an exact hit on this node beats an owner inherited from above.
        var own = MatchSelf(node);
        if (own != null)
        {
            box.Program = own;
            box.IsRoot = true;
        }
        else if (parentBox?.Program != null)
        {
            box.Program = parentBox.Program;
        }

        _memo.Add(node, box);
        return box;
    }

    private InstalledProgram? MatchSelf(ScanNode node)
    {
        var name = node.Parent == null ? LastSegment(node.Name) : node.Name;

        // Install location: only build the full path when the folder name could be a root.
        if (node.IsDirectory && _rootNames.Contains(name))
        {
            var full = NormalizeLocation(node.FullPath);
            if (full != null && _byLocation.TryGetValue(full, out var p)) return p;
        }

        if (!node.IsDirectory || _storeByFamily.Count == 0) return null;

        // %LOCALAPPDATA%\Packages\<family name>
        if (node.Parent?.Name == "Packages" && _storeByFamily.TryGetValue(name, out var fam)) return fam;

        // Program Files\WindowsApps\<name>_<version>_<arch>_<resourceId>_<publisherId>
        if (node.Parent?.Name == "WindowsApps")
        {
            var parts = name.Split('_');
            if (parts.Length >= 5 && _storeByPackageKey.TryGetValue(parts[0] + "_" + parts[^1], out var pkg))
                return pkg;
        }
        return null;
    }

    public Explanation? Explain(ScanNode node)
    {
        var box = Resolve(node);
        var p = box.Program;
        if (p == null) return ExplainVendorFolder(node);

        var publisher = string.IsNullOrWhiteSpace(p.Publisher) ? null : p.Publisher;
        var label = publisher == null ? p.DisplayName : $"{p.DisplayName} ({publisher})";
        var kind = p.IsStoreApp ? "Store app" : "program";
        var version = string.IsNullOrWhiteSpace(p.Version) ? "" : $" Version {p.Version}.";

        string title, summary;
        if (box.IsRoot)
        {
            title = p.DisplayName;
            summary = $"Installed {kind}: {label}";
        }
        else
        {
            title = $"Part of {p.DisplayName}";
            summary = $"Part of installed {kind}: {label}";
        }

        return new Explanation(
            title,
            Badge.App,
            publisher,
            summary,
            $"This folder belongs to {p.DisplayName}, a {kind} installed on this computer.{version}",
            "Installed programs keep their binaries, assets, runtimes and updates in their install folder.",
            "The program will stop working; uninstall it properly instead",
            "Uninstall it from Settings > Apps",
            new SaferAction("settingsUri", "Open Installed apps", "ms-settings:appsfeatures"),
            ExplanationSource.InstalledProgram,
            0.95,
            null);
    }

    // A direct child of a Program Files root that only contains install roots ("Google" holding Chrome).
    private Explanation? ExplainVendorFolder(ScanNode node)
    {
        if (_vendorFolders.Count == 0 || !node.IsDirectory || node.Parent == null) return null;
        if (!_vendorNames.Contains(node.Name)) return null;
        var full = NormalizeLocation(node.FullPath);
        if (full == null || !_vendorFolders.TryGetValue(full, out var list)) return null;

        var owner = list.Where(x => !string.IsNullOrWhiteSpace(x.Publisher))
            .GroupBy(x => x.Publisher!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? node.Name;

        string summary;
        if (list.Count == 1) summary = $"Contains {list[0].DisplayName}";
        else
        {
            summary = $"Contains {list.Count} installed programs: " + string.Join(", ", list.Select(x => x.DisplayName));
            if (summary.Length > 90) summary = summary.Substring(0, 89) + "…";
        }

        var shown = list.Take(5).Select(x => string.IsNullOrWhiteSpace(x.Version) ? x.DisplayName : $"{x.DisplayName} {x.Version}");
        var more = list.Count > 5 ? $" and {list.Count - 5} more" : "";
        return new Explanation(
            node.Name,
            Badge.App,
            owner,
            summary,
            $"This vendor folder holds the install folders of: {string.Join(", ", shown)}{more}.",
            "Some vendors group their programs under one folder in Program Files.",
            "These programs stop working; uninstall them from Settings > Apps instead",
            "Uninstall the programs you don't need from Settings > Apps",
            new SaferAction("settingsUri", "Open Installed apps", "ms-settings:appsfeatures"),
            ExplanationSource.InstalledProgram,
            1.0,
            null);
    }
}
