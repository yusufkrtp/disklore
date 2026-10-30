using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.App.Services;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Scanning;

namespace DiskLore.App.ViewModels.Tabs;

public sealed class LargestFileRow
{
    public LargestFileRow(int rank, ScanNode node, Explanation explanation)
    {
        Rank = rank;
        Node = node;
        Name = node.Name;
        SizeText = SizeFormat.Format(node.Size);
        Badge = explanation.Badge;
        Summary = explanation.Summary;
        var parent = node.Parent?.FullPath ?? "";
        ParentFull = parent;
        ParentText = TabText.MiddleTrim(parent, 48);
        ModifiedText = TabText.Date(node.LastWriteUtc);
    }

    public int Rank { get; }
    public ScanNode Node { get; }
    public string Name { get; }
    public string SizeText { get; }
    public Badge Badge { get; }
    public string Summary { get; }
    public string ParentFull { get; }
    public string ParentText { get; }
    public string ModifiedText { get; }
}

public sealed partial class LargestFilesViewModel : ObservableObject
{
    private int _version;
    private IReadOnlyList<ScanNode> _nodes = [];

    [ObservableProperty] private IReadOnlyList<LargestFileRow> _rows = [];
    [ObservableProperty] private string _status = "Scan a drive or folder to see its largest files.";
    [ObservableProperty] private bool _hasRows;

    // Recomputes the list off the UI thread; a newer call makes older ones ignore their result.
    public async Task LoadAsync(IAppContext? ctx)
    {
        var version = ++_version;
        var root = ctx?.Current?.Root;
        if (ctx == null || root == null)
        {
            _nodes = [];
            Show([]);
            Status = "Scan a drive or folder to see its largest files.";
            return;
        }

        Status = "Finding the largest files…";
        try
        {
            var nodes = await Task.Run(() => SubtreeAnalyzer.LargestFiles(root, 100));
            if (version != _version) return;
            _nodes = nodes;
            Show(BuildRows(ctx));
            Status = nodes.Count == 0 ? "No files found in this scan." : "";
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            _nodes = [];
            Show([]);
            Status = "Could not list the largest files: " + ex.Message;
        }
    }

    // Same nodes, fresh explanations (after Claude answered something).
    public void RefreshExplanations(IAppContext? ctx)
    {
        if (ctx == null || _nodes.Count == 0) return;
        Show(BuildRows(ctx));
    }

    private List<LargestFileRow> BuildRows(IAppContext ctx)
    {
        var rows = new List<LargestFileRow>(_nodes.Count);
        for (var i = 0; i < _nodes.Count; i++)
            rows.Add(new LargestFileRow(i + 1, _nodes[i], ctx.Explanations.Explain(_nodes[i])));
        return rows;
    }

    private void Show(IReadOnlyList<LargestFileRow> rows)
    {
        Rows = rows;
        HasRows = rows.Count > 0;
    }
}
