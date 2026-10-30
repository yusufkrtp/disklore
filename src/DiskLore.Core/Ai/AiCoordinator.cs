using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Ai;

// Picks unexplained items worth asking Claude about and feeds the answers back into ExplanationService.
public sealed class AiCoordinator
{
    public AiCoordinator(ClaudeCliExplainer explainer, ExplanationService explanations)
    {
    }

    public bool IsAvailable => throw new NotImplementedException();

    // Unknown (Source.None) folders and files with Size >= thresholdBytes, largest first, at most maxItems.
    public IReadOnlyList<ScanNode> SelectCandidates(ScanNode root, long thresholdBytes, int maxItems)
        => throw new NotImplementedException();

    // Builds the request items, asks Claude in batches, invalidates answered nodes. Returns the number answered.
    public Task<int> ExplainAsync(IReadOnlyList<ScanNode> nodes, IProgress<string>? status, CancellationToken ct)
        => throw new NotImplementedException();
}
