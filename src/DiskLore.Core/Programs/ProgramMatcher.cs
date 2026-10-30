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

    public ProgramMatcher(IReadOnlyList<InstalledProgram> programs)
    {
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
        if (p == null) return null;

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
}
