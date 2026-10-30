using System.Diagnostics;
using System.Globalization;
using System.Text;
using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Programs;
using DiskLore.Core.Rules;
using DiskLore.Core.Scanning;

// Measures how much of a real disk the knowledge base explains.
//
//   dotnet run --project tools/DiskLore.Coverage -- [path] [--out dir] [--min-gb 1] [--personal path]...
//
// Scans <path> (default: the system drive), explains every node the way the app does, but with an
// empty AI cache so cached answers cannot hide gaps (the AI is never called), and writes TSV reports
// to <repo>/artifacts/coverage. --personal marks folders holding the owner's own projects or data;
// unknown items under them are counted separately because no rule should name them.

var opts = Options.Parse(args);
var outDir = opts.OutDir ?? Path.Combine(FindRepoRoot() ?? Directory.GetCurrentDirectory(), "artifacts", "coverage");
Directory.CreateDirectory(outDir);

Console.WriteLine($"Scanning {opts.Root} ...");
var sw = Stopwatch.StartNew();
var scan = await new Scanner().ScanAsync(opts.Root, null, CancellationToken.None);
Console.WriteLine($"Scanned {SizeFormat(scan.Root.Size)} in {sw.Elapsed.TotalSeconds:F0} s, {scan.UnreadablePaths.Count} unreadable folders.");

var rules = RuleSet.Load(null);
if (rules.Errors.Count > 0)
{
    Console.WriteLine($"Rule errors ({rules.Errors.Count}):");
    foreach (var e in rules.Errors) Console.WriteLine("  " + e);
}

var programs = PlatformServices.Create().Programs.GetInstalled();
var aiCachePath = Path.Combine(Path.GetTempPath(), "disklore-coverage-" + Guid.NewGuid().ToString("N") + ".json");
var service = new ExplanationService(rules, new ProgramMatcher(programs), new LeftoverHeuristic(programs), new AiCache(aiCachePath));
Console.WriteLine($"{rules.Count} rules, {programs.Count} installed programs.");

long minBytes = (long)(opts.MinGb * 1024 * 1024 * 1024);
var personal = opts.Personal.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToList();
var systemRoots = new[]
{
    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
}.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var appDataRoots = new[]
{
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
}.Where(s => !string.IsNullOrEmpty(s)).ToList();

var big = new List<(ScanNode Node, Explanation E)>();
var suspicious = new List<(ScanNode Node, Explanation E, string Why)>();
var systemVerdicts = new Dictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);

// Depth-first walk carrying the path so FullPath is not rebuilt for every node.
var stack = new Stack<(ScanNode Node, string Path)>();
stack.Push((scan.Root, scan.Root.Name));
while (stack.Count > 0)
{
    var (node, path) = stack.Pop();
    foreach (var c in node.Children)
        if (c.IsDirectory) stack.Push((c, Path.Combine(path, c.Name)));
        else if (c.Size >= minBytes || InAny(Path.Combine(path, c.Name), systemRoots)) Visit(c, Path.Combine(path, c.Name));
    Visit(node, path);
}

// Every node >= the threshold, every node in a system folder, and every direct child of the
// per-user AppData folders (where leftovers are found) is explained.
void Visit(ScanNode node, string path)
{
    bool isBig = node.Size >= minBytes && node.Parent != null;
    bool inSystem = InAny(path, systemRoots);
    bool appDataChild = node.Parent != null && InAny(Path.GetDirectoryName(path) ?? "", appDataRoots, exact: true);
    if (!isBig && !inSystem && !appDataChild) return;

    var e = service.Explain(node);
    bool own = e.Source != ExplanationSource.Inherited;
    if (isBig)
    {
        big.Add((node, e));
        if (e.Badge == Badge.Safe && own) suspicious.Add((node, e, "safe >= threshold"));
    }
    if (inSystem && e.Badge is Badge.Safe or Badge.Leftover && own)
    {
        var key = $"{e.Badge}\t{e.Source}\t{e.RuleId ?? "-"}";
        systemVerdicts.TryGetValue(key, out var acc);
        systemVerdicts[key] = (acc.Count + 1, acc.Bytes + node.Size);
        if (!(isBig && e.Badge == Badge.Safe)) suspicious.Add((node, e, "safe/leftover in a system folder"));
    }
    else if (!inSystem && e.Badge == Badge.Leftover)
        suspicious.Add((node, e, "leftover"));
}

// ---- reports ----
big.Sort((a, b) => b.Node.Size.CompareTo(a.Node.Size));
WriteTsv(Path.Combine(outDir, "big-items.tsv"), big.Select(b => Row(b.Node, b.E)));

bool IsPersonal(ScanNode n) => InAny(n.FullPath, personal);
var unknown = big.Where(b => b.E.Badge == Badge.Unknown).ToList();
var unknownPersonal = unknown.Where(b => IsPersonal(b.Node)).ToList();
var unknownOther = unknown.Except(unknownPersonal).ToList();
long bigBytes = big.Sum(b => b.Node.Size);

Console.WriteLine();
Console.WriteLine($"== Items >= {opts.MinGb} GB (scan root excluded) ==");
Console.WriteLine($"items: {big.Count}, {SizeFormat(bigBytes)}");
Console.WriteLine($"unknown: {unknown.Count} ({Pct(unknown.Count, big.Count)}), {SizeFormat(unknown.Sum(u => u.Node.Size))} ({Pct(unknown.Sum(u => u.Node.Size), bigBytes)} of bytes)");
Console.WriteLine($"  of which under --personal folders: {unknownPersonal.Count}; elsewhere: {unknownOther.Count} ({Pct(unknownOther.Count, big.Count)})");
foreach (var g in big.GroupBy(b => b.E.Source).OrderBy(g => g.Key))
    Console.WriteLine($"  source {g.Key,-16} {g.Count(),5}");
foreach (var g in big.GroupBy(b => b.E.Badge).OrderBy(g => g.Key))
    Console.WriteLine($"  badge  {g.Key,-16} {g.Count(),5}");

Console.WriteLine();
Console.WriteLine("Unknown items:");
foreach (var u in unknown)
    Console.WriteLine($"  {SizeFormat(u.Node.Size),10}  {(IsPersonal(u.Node) ? "[personal] " : "")}{u.Node.FullPath}");

// Acceptance: direct children of the well-known folders must all be explained.
var topFolders = new[]
{
    Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "",
    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
}.Where(s => s.Length > 0).ToList();

var topRows = new List<string[]>();
var topUnknown = new List<string>();
foreach (var folder in topFolders)
{
    var node = Find(scan.Root, folder);
    if (node == null)
    {
        Console.WriteLine($"(not in scan: {folder})");
        continue;
    }
    foreach (var c in node.Children)
    {
        var e = service.Explain(c);
        topRows.Add(Row(c, e));
        if (e.Badge == Badge.Unknown) topUnknown.Add($"{SizeFormat(c.Size),10}  {c.FullPath}");
    }
}
WriteTsv(Path.Combine(outDir, "top-level.tsv"), topRows);
Console.WriteLine();
Console.WriteLine($"== Top-level children without a badge: {topUnknown.Count} of {topRows.Count} ==");
foreach (var t in topUnknown) Console.WriteLine("  " + t);

suspicious.Sort((a, b) => b.Node.Size.CompareTo(a.Node.Size));
WriteTsv(Path.Combine(outDir, "suspicious.tsv"), suspicious.Select(s => Row(s.Node, s.E).Prepend(s.Why).ToArray()));
Console.WriteLine();
Console.WriteLine($"== Suspicious verdicts: {suspicious.Count} (full list in suspicious.tsv) ==");
foreach (var group in suspicious.GroupBy(s => s.Why))
{
    Console.WriteLine($"-- {group.Key}: {group.Count()}");
    foreach (var s in group.Take(40))
        Console.WriteLine($"  {SizeFormat(s.Node.Size),10}  {s.E.Badge,-8} {s.E.Source,-16} {s.E.RuleId ?? "-",-34} {s.Node.FullPath}");
}

Console.WriteLine();
Console.WriteLine("== Safe/leftover verdicts in system folders, by rule (own verdicts, any size) ==");
foreach (var kv in systemVerdicts.OrderByDescending(kv => kv.Value.Bytes))
    Console.WriteLine($"  {kv.Value.Count,7}  {SizeFormat(kv.Value.Bytes),10}  {kv.Key.Replace('\t', ' ')}");

try { File.Delete(aiCachePath); } catch { }
Console.WriteLine();
Console.WriteLine($"Reports written to {outDir}");
return 0;

// ---- helpers ----

static string[] Row(ScanNode n, Explanation e) =>
[
    n.Size.ToString(CultureInfo.InvariantCulture),
    (n.Size / 1073741824.0).ToString("F2", CultureInfo.InvariantCulture),
    n.IsDirectory ? "dir" : "file",
    e.Badge.ToString(),
    e.Source.ToString(),
    e.RuleId ?? "",
    e.Title,
    n.FullPath,
];

static void WriteTsv(string file, IEnumerable<string[]> rows)
{
    var sb = new StringBuilder();
    foreach (var r in rows)
        sb.AppendJoin('\t', r.Select(c => c.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '))).Append('\n');
    File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
}

static bool InAny(string path, IReadOnlyList<string> roots, bool exact = false)
{
    foreach (var r in roots)
    {
        if (path.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
        if (!exact && path.Length > r.Length && path.StartsWith(r, StringComparison.OrdinalIgnoreCase) && path[r.Length] is '\\' or '/')
            return true;
    }
    return false;
}

static ScanNode? Find(ScanNode root, string path)
{
    var rootPath = Path.TrimEndingDirectorySeparator(root.Name);
    var target = Path.TrimEndingDirectorySeparator(path);
    if (target.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) return root;
    if (!target.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) return null;
    var node = root;
    foreach (var seg in target[rootPath.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
    {
        node = node.Children.FirstOrDefault(c => c.Name.Equals(seg, StringComparison.OrdinalIgnoreCase));
        if (node == null) return null;
    }
    return node;
}

static string SizeFormat(long bytes) => bytes switch
{
    >= 1L << 30 => (bytes / 1073741824.0).ToString("F1", CultureInfo.InvariantCulture) + " GB",
    >= 1L << 20 => (bytes / 1048576.0).ToString("F0", CultureInfo.InvariantCulture) + " MB",
    _ => (bytes / 1024.0).ToString("F0", CultureInfo.InvariantCulture) + " KB",
};

static string Pct(double part, double whole) =>
    whole <= 0 ? "0%" : (100.0 * part / whole).ToString("F1", CultureInfo.InvariantCulture) + "%";

static string? FindRepoRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "DiskLore.sln"))) return d.FullName;
    return null;
}

sealed class Options
{
    public string Root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) is { Length: > 0 } r ? r : "/";
    public string? OutDir;
    public double MinGb = 1;
    public readonly List<string> Personal = new();

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": o.OutDir = args[++i]; break;
                case "--min-gb": o.MinGb = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--personal": o.Personal.Add(args[++i]); break;
                default: o.Root = args[i]; break;
            }
        }
        return o;
    }
}
