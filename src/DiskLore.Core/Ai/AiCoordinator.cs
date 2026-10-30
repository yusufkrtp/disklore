using DiskLore.Core.Ai.Providers;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Scanning;

namespace DiskLore.Core.Ai;

// Picks unexplained items worth ask the AI and feeds the answers back into ExplanationService.
public sealed class AiCoordinator
{
    private const int BatchSize = 5;
    private const int MaxSampleNames = 30;

    private readonly AiExplainer _explainer;
    private readonly Func<ScanNode, Explanation> _explain;
    private readonly Action<ScanNode> _invalidate;

    public AiCoordinator(AiExplainer explainer, ExplanationService explanations)
        : this(explainer, explanations.Explain, explanations.Invalidate)
    {
    }

    // Test seam: lets tests supply explanations without the real service.
    internal AiCoordinator(AiExplainer explainer, Func<ScanNode, Explanation> explain, Action<ScanNode> invalidate)
    {
        _explainer = explainer;
        _explain = explain;
        _invalidate = invalidate;
    }

    public bool IsAvailable => _explainer.IsAvailable;
    public string ProviderName => _explainer.ProviderShortName;
    public string? LastError => _explainer.LastError;

    // Unknown (Source.None) folders and files with Size >= thresholdBytes, largest first, at most maxItems.
    // The scan root itself is never a candidate.
    public IReadOnlyList<ScanNode> SelectCandidates(ScanNode root, long thresholdBytes, int maxItems)
    {
        var result = new List<ScanNode>();
        if (maxItems <= 0) return result;

        var stack = new Stack<ScanNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.IsReparsePoint) continue;

            if (!ReferenceEquals(node, root) && !node.AccessDenied && _explain(node).Source == ExplanationSource.None)
                result.Add(node);

            if (!node.IsDirectory) continue;
            // Children are sorted by size descending: stop at the first one below the threshold.
            foreach (var child in node.Children)
            {
                if (child.Size < thresholdBytes) break;
                stack.Push(child);
            }
        }

        return result.OrderByDescending(n => n.Size).Take(maxItems).ToList();
    }

    // Builds the request items, asks the provider in batches, invalidates answered nodes. Returns the number answered.
    public async Task<int> ExplainAsync(IReadOnlyList<ScanNode> nodes, IProgress<string>? status, CancellationToken ct)
    {
        var answered = 0;
        var batches = (nodes.Count + BatchSize - 1) / BatchSize;
        for (var b = 0; b < batches; b++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = nodes.Skip(b * BatchSize).Take(BatchSize).ToList();
            var noun = chunk.All(n => n.IsDirectory) ? "folder" : chunk.All(n => !n.IsDirectory) ? "file" : "item";
            status?.Report($"Asking {_explainer.ProviderShortName} about {chunk.Count} {noun}{(chunk.Count == 1 ? "" : "s")} ({b + 1}/{batches})…");

            var items = chunk.Select(n => (Node: n, Item: BuildItem(n))).ToList();
            var answers = await _explainer.ExplainAsync(items, ct).ConfigureAwait(false);
            foreach (var node in chunk)
            {
                if (!answers.ContainsKey(node.FullPath)) continue;
                answered++;
                _invalidate(node);
            }
        }
        return answered;
    }

    internal AiRequestItem BuildItem(ScanNode node)
    {
        IReadOnlyList<NamedSize> topChildren;
        IReadOnlyList<NamedSize> topExtensions;
        long fileCount = node.FileCount;
        if (node.IsDirectory)
        {
            try
            {
                var stats = SubtreeAnalyzer.Analyze(node);
                topChildren = stats.TopChildren;
                topExtensions = stats.TopExtensions;
                fileCount = stats.FileCount;
            }
            catch (Exception)
            {
                // SubtreeAnalyzer unavailable: direct children only, no extension list.
                topChildren = node.Children.Take(5).Select(c => new NamedSize(c.Name, c.Size)).ToList();
                topExtensions = [];
            }
        }
        else
        {
            topChildren = [];
            topExtensions = node.Extension.Length > 0 ? [new NamedSize(node.Extension, node.Size)] : [];
        }

        string? parentTitle = null;
        if (node.Parent is { } parent)
        {
            var pe = _explain(parent);
            if (pe.Source != ExplanationSource.None) parentTitle = pe.Title;
        }

        return new AiRequestItem(
            ReplaceProfile(node.FullPath), node.Size, fileCount,
            topChildren, topExtensions, SampleNames(node), null, parentTitle);
    }

    internal static string ReplaceProfile(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        if (profile.Length == 0) return path;
        var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.StartsWith(profile, cmp) && (path.Length == profile.Length || path[profile.Length] is '\\' or '/'))
            return (OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~") + path[profile.Length..];
        return path;
    }

    // Up to 30 names: the largest descendants first, topped up with a spread of direct children.
    internal static IReadOnlyList<string> SampleNames(ScanNode node)
    {
        var names = new List<string>();
        if (!node.IsDirectory || node.Children.Count == 0) return names;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int largestTarget = 18;

        // Best-first walk: each popped node contributes its few largest children (children are sorted by size).
        var queue = new PriorityQueue<ScanNode, long>();
        void Enqueue(ScanNode parent)
        {
            var take = 0;
            foreach (var c in parent.Children)
            {
                queue.Enqueue(c, -c.Size);
                if (++take >= largestTarget) break;
            }
        }
        Enqueue(node);
        var popped = 0;
        while (queue.Count > 0 && names.Count < largestTarget && popped < 400)
        {
            var n = queue.Dequeue();
            popped++;
            if (seen.Add(n.Name)) names.Add(n.Name);
            if (n.IsDirectory) Enqueue(n);
        }

        // Spread of direct children (evenly spaced through the size-sorted list).
        var direct = node.Children;
        var remaining = MaxSampleNames - names.Count;
        if (remaining > 0 && direct.Count > 0)
        {
            var step = Math.Max(1.0, direct.Count / (double)remaining);
            for (double i = 0; i < direct.Count && names.Count < MaxSampleNames; i += step)
            {
                var c = direct[(int)i];
                if (seen.Add(c.Name)) names.Add(c.Name);
            }
            for (var i = 0; i < direct.Count && names.Count < MaxSampleNames; i++)
                if (seen.Add(direct[i].Name)) names.Add(direct[i].Name);
        }
        return names;
    }
}
