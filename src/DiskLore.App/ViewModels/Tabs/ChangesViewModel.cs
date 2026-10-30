using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.App.Services;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Snapshots;

namespace DiskLore.App.ViewModels.Tabs;

public sealed class SnapshotChoice
{
    public SnapshotChoice(SnapshotInfo info)
    {
        Info = info;
        Label = $"{TabText.DateAndTime(info.TakenUtc)} — {SizeFormat.Format(info.TotalSize)}";
    }

    public SnapshotInfo Info { get; }
    public string Label { get; }
}

public sealed class ChangeRow
{
    public ChangeRow(DiffEntry entry, ScanNode? node, Explanation? explanation)
    {
        Entry = entry;
        Node = node;
        RelativePath = entry.RelativePath.Length == 0 ? "(whole location)" : entry.RelativePath;
        PathText = TabText.MiddleTrim(RelativePath, 70);
        OldText = entry.OldSize == 0 ? "—" : SizeFormat.Format(entry.OldSize);
        NewText = entry.NewSize == 0 ? "—" : SizeFormat.Format(entry.NewSize);
        DeltaText = SizeFormat.FormatDelta(entry.Delta);
        IsGrowth = entry.Delta > 0;
        Tag = entry.OldSize == 0 ? "new" : entry.NewSize == 0 ? "removed" : "";
        HasBadge = explanation != null;
        Badge = explanation?.Badge ?? Badge.Unknown;
        Summary = explanation?.Summary ?? "";
    }

    public DiffEntry Entry { get; }
    public ScanNode? Node { get; }
    public string RelativePath { get; }
    public string PathText { get; }
    public string OldText { get; }
    public string NewText { get; }
    public string DeltaText { get; }
    public bool IsGrowth { get; }
    public bool IsShrink => !IsGrowth;
    public string Tag { get; }
    public bool HasTag => Tag.Length > 0;
    public bool HasBadge { get; }
    public Badge Badge { get; }
    public string Summary { get; }
}

public sealed partial class ChangesViewModel : ObservableObject
{
    private const int MaxRows = 500;
    private int _version;
    private IAppContext? _ctx;
    private bool _suppressSelection;

    [ObservableProperty] private IReadOnlyList<SnapshotChoice> _snapshots = [];
    [ObservableProperty] private SnapshotChoice? _selectedSnapshot;
    [ObservableProperty] private IReadOnlyList<ChangeRow> _rows = [];
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _emptyText = "";
    [ObservableProperty] private bool _hasContent;
    [ObservableProperty] private bool _hasRows;

    internal const string NoSnapshotText =
        "No earlier scan of this location yet. DiskLore saves a snapshot after every full drive scan; come back after your next scan to see what grew.";

    // Reloads the snapshot list for the current scan, then the diff against the newest earlier one.
    public async Task LoadAsync(IAppContext? ctx)
    {
        _ctx = ctx;
        var version = ++_version;
        var current = ctx?.Current;
        if (ctx == null || current == null)
        {
            ShowEmpty("Scan a drive or folder first. Changes compares it with your earlier scans.");
            return;
        }

        try
        {
            var root = current.Root;
            var started = current.StartedUtc;
            var list = await Task.Run(() => ctx.Snapshots.List(root.FullPath));
            if (version != _version) return;

            // The snapshot saved for this very scan is written after the scan ends, so it is not older than the
            // moment the scan began. Only snapshots taken before the scan started count as "earlier".
            var older = list.Where(s => s.TakenUtc < started).Select(s => new SnapshotChoice(s)).ToList();
            if (older.Count == 0)
            {
                ShowEmpty(NoSnapshotText);
                return;
            }

            _suppressSelection = true;
            Snapshots = older;
            SelectedSnapshot = older[0];
            _suppressSelection = false;
            await ComputeAsync(older[0], version);
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            _suppressSelection = false;
            ShowEmpty("Could not read the earlier scans: " + ex.Message);
        }
    }

    partial void OnSelectedSnapshotChanged(SnapshotChoice? value)
    {
        if (_suppressSelection || value == null || _ctx?.Current == null) return;
        var version = ++_version;
        _ = ComputeAsync(value, version);
    }

    private async Task ComputeAsync(SnapshotChoice choice, int version)
    {
        var ctx = _ctx;
        var root = ctx?.Current?.Root;
        if (ctx == null || root == null) return;

        Summary = "Comparing…";
        HasContent = true;
        HasRows = false;
        Rows = [];
        try
        {
            var entries = await Task.Run(() =>
            {
                var snapshot = ctx.Snapshots.Load(choice.Info);
                return SnapshotDiff.Compare(snapshot, root);
            });
            if (version != _version) return;

            var shown = entries.Count <= MaxRows
                ? entries
                : entries.Take(MaxRows * 3 / 5).Concat(entries.Skip(entries.Count - MaxRows * 2 / 5)).ToList();
            var rows = shown.Select(e =>
            {
                var node = FindNode(root, e.RelativePath);
                return new ChangeRow(e, node, node == null ? null : ctx.Explanations.Explain(node));
            }).ToList();

            Rows = rows;
            HasRows = rows.Count > 0;
            var total = root.Size - choice.Info.TotalSize;
            var since = TabText.Date(choice.Info.TakenUtc);
            Summary = rows.Count == 0
                ? $"No folder changed by 50 MB or more since {since}."
                : $"{SizeFormat.FormatDelta(total)} since {since}";
            HasContent = true;
            EmptyText = "";
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            ShowEmpty("Could not compare with that scan: " + ex.Message);
        }
    }

    private void ShowEmpty(string text)
    {
        Snapshots = [];
        SelectedSnapshot = null;
        Rows = [];
        HasRows = false;
        HasContent = false;
        Summary = "";
        EmptyText = text;
    }

    // Walks the relative path segment by segment from the scan root.
    internal static ScanNode? FindNode(ScanNode root, string relativePath)
    {
        if (relativePath.Length == 0) return root;
        var node = root;
        foreach (var segment in relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            ScanNode? next = null;
            foreach (var c in node.Children)
            {
                if (string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = c;
                    break;
                }
            }
            if (next == null) return null;
            node = next;
        }
        return node;
    }
}
