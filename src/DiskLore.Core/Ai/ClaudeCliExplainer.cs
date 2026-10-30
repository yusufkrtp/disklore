using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Ai;

public sealed class ClaudeCliExplainer
{
    public ClaudeCliExplainer(AiCache cache, string model = "sonnet")
    {
    }

    // True when the claude CLI is found on PATH.
    public bool IsAvailable => throw new NotImplementedException();

    // Batches of up to 5 items per CLI call. Returns full path -> explanation and writes every answer to the cache.
    public Task<IReadOnlyDictionary<string, Explanation>> ExplainAsync(
        IReadOnlyList<(ScanNode Node, AiRequestItem Item)> items, CancellationToken ct)
        => throw new NotImplementedException();
}
