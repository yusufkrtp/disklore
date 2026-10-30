using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.ViewModels;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void InsertRange(int index, IList<T> items)
    {
        if (items.Count == 0) return;
        for (var i = 0; i < items.Count; i++) Items.Insert(index + i, items[i]);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList(), index));
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0) return;
        var removed = new List<T>(count);
        for (var i = 0; i < count; i++) removed.Add(Items[index + i]);
        for (var i = 0; i < count; i++) Items.RemoveAt(index);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }
}

public enum TreeSort { Name, Size, Changed }

// Flattened, virtualization-friendly view of the scan tree: expand/collapse inserts or removes rows in place.
public sealed class TreeModel : ObservableObject
{
    private readonly HashSet<ScanNode> _expanded = new();
    private readonly Dictionary<ScanNode, int> _limits = new();
    private readonly Dictionary<ScanNode, TreeRowViewModel> _cache = new();
    private Func<ScanNode, Explanation>? _explain;
    private TreeSort _sort = TreeSort.Size;
    private bool _descending = true;

    public BulkObservableCollection<TreeRowViewModel> Rows { get; } = new();
    public ScanNode? Root { get; private set; }
    public int PageSize { get; set; } = 500;

    public TreeSort SortBy => _sort;
    public bool Descending => _descending;
    public string NameHeader => Header("Name", TreeSort.Name);
    public string SizeHeader => Header("Size", TreeSort.Size);
    public string ChangedHeader => Header("Last changed", TreeSort.Changed);

    private string Header(string title, TreeSort s) => _sort == s ? title + (_descending ? " ↓" : " ↑") : title;

    public void Load(ScanNode? root, Func<ScanNode, Explanation>? explain)
    {
        Root = root;
        _explain = explain;
        _expanded.Clear();
        _limits.Clear();
        _cache.Clear();
        if (root != null) _expanded.Add(root);
        Rebuild();
    }

    public void Rebuild()
    {
        var list = new List<TreeRowViewModel>();
        if (Root != null)
        {
            var rootRow = GetRow(Root, 0);
            list.Add(rootRow);
            if (rootRow.IsExpanded) AddChildren(list, Root, 1, 0);
        }
        Rows.ReplaceAll(list);
    }

    // After sizes changed (delete): rebuild the list and refresh every number.
    public void RefreshAfterChange()
    {
        foreach (var r in _cache.Values) r.RefreshNumbers();
        Rebuild();
    }

    public TreeRowViewModel? FindRow(ScanNode node)
        => _cache.TryGetValue(node, out var r) && Rows.Contains(r) ? r : null;

    public void RefreshExplanation(ScanNode node)
    {
        if (_cache.TryGetValue(node, out var r)) r.InvalidateExplanation();
    }

    public void RefreshAllExplanations()
    {
        foreach (var r in _cache.Values) r.InvalidateExplanation();
    }

    public void Toggle(TreeRowViewModel row)
    {
        if (row.IsExpanded) Collapse(row); else Expand(row);
    }

    public void Expand(TreeRowViewModel row)
    {
        if (row.Node == null || !row.HasChildren || row.IsExpanded) return;
        _expanded.Add(row.Node);
        row.IsExpanded = true;
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        var list = new List<TreeRowViewModel>();
        AddChildren(list, row.Node, row.Depth + 1, 0);
        Rows.InsertRange(index + 1, list);
    }

    public void Collapse(TreeRowViewModel row)
    {
        if (row.Node == null || !row.IsExpanded) return;
        _expanded.Remove(row.Node);
        row.IsExpanded = false;
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        var end = index + 1;
        while (end < Rows.Count && Rows[end].Depth > row.Depth) end++;
        Rows.RemoveRange(index + 1, end - index - 1);
    }

    // Replaces the "Show N more" row with the next page. Returns the first new row (or null).
    public TreeRowViewModel? ShowMore(TreeRowViewModel moreRow)
    {
        if (!moreRow.IsShowMore || moreRow.MoreParent == null) return null;
        var parent = moreRow.MoreParent;
        var index = Rows.IndexOf(moreRow);
        if (index < 0) return null;
        var oldLimit = LimitFor(parent);
        _limits[parent] = oldLimit + PageSize;
        Rows.RemoveRange(index, 1);
        var list = new List<TreeRowViewModel>();
        AddChildren(list, parent, moreRow.Depth, oldLimit);
        Rows.InsertRange(index, list);
        return list.Count > 0 ? list[0] : null;
    }

    // Makes sure every ancestor is expanded and the node is within its page, then returns its row.
    public TreeRowViewModel? ExpandTo(ScanNode node)
    {
        if (Root == null) return null;
        var chain = new List<ScanNode>();
        for (var n = node; n != null; n = n.Parent)
        {
            chain.Add(n);
            if (ReferenceEquals(n, Root)) break;
        }
        if (!ReferenceEquals(chain[^1], Root)) return null;
        chain.Reverse();

        var changed = false;
        for (var i = 0; i < chain.Count - 1; i++)
        {
            var anc = chain[i];
            if (_expanded.Add(anc)) changed = true;
            var child = chain[i + 1];
            var pos = IndexOfSorted(anc, child);
            if (pos >= LimitFor(anc))
            {
                _limits[anc] = (pos / PageSize + 1) * PageSize;
                changed = true;
            }
        }
        if (changed) Rebuild();
        return FindRow(node);
    }

    public void SetSort(TreeSort column)
    {
        if (_sort == column) _descending = !_descending;
        else
        {
            _sort = column;
            _descending = column != TreeSort.Name;
        }
        _limits.Clear();
        Rebuild();
        OnPropertyChanged(nameof(SortBy));
        OnPropertyChanged(nameof(Descending));
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(SizeHeader));
        OnPropertyChanged(nameof(ChangedHeader));
    }

    // Parent row for keyboard navigation (Left on a collapsed row).
    public TreeRowViewModel? ParentRow(TreeRowViewModel row)
    {
        var p = row.Node?.Parent ?? row.MoreParent;
        return p == null ? null : FindRow(p);
    }

    public IReadOnlyList<ScanNode> SortedChildren(ScanNode node)
    {
        var kids = node.Children;
        if (kids.Count < 2) return kids;
        if (_sort == TreeSort.Size && _descending) return kids;
        IEnumerable<ScanNode> q = _sort switch
        {
            TreeSort.Name => _descending
                ? kids.OrderByDescending(k => k.Name, StringComparer.OrdinalIgnoreCase)
                : kids.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase),
            TreeSort.Size => kids.OrderBy(k => k.Size),
            _ => _descending ? kids.OrderByDescending(k => k.LastWriteUtc) : kids.OrderBy(k => k.LastWriteUtc),
        };
        return q.ToList();
    }

    private int IndexOfSorted(ScanNode parent, ScanNode child)
    {
        var list = SortedChildren(parent);
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], child)) return i;
        return int.MaxValue;
    }

    private int LimitFor(ScanNode parent) => _limits.TryGetValue(parent, out var l) ? l : PageSize;

    private TreeRowViewModel GetRow(ScanNode node, int depth)
    {
        if (!_cache.TryGetValue(node, out var row))
        {
            row = new TreeRowViewModel(node, depth, _explain);
            _cache[node] = row;
        }
        row.Depth = depth;
        row.IsExpanded = row.HasChildren && _expanded.Contains(node);
        return row;
    }

    private void AddChildren(List<TreeRowViewModel> list, ScanNode parent, int depth, int start)
    {
        var kids = SortedChildren(parent);
        var limit = Math.Min(kids.Count, LimitFor(parent));
        for (var i = start; i < limit; i++)
        {
            var row = GetRow(kids[i], depth);
            list.Add(row);
            if (row.IsExpanded) AddChildren(list, kids[i], depth + 1, 0);
        }
        if (limit < kids.Count)
        {
            long bytes = 0;
            for (var i = limit; i < kids.Count; i++) bytes += kids[i].Size;
            list.Add(TreeRowViewModel.CreateShowMore(parent, depth, kids.Count - limit, bytes, PageSize));
        }
    }
}
