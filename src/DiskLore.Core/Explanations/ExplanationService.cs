using DiskLore.Core.Ai;
using DiskLore.Core.Model;
using DiskLore.Core.Programs;
using DiskLore.Core.Rules;

namespace DiskLore.Core.Explanations;

public sealed class ExplanationService
{
    // Order: RuleSet -> ProgramMatcher -> LeftoverHeuristic -> AiCache -> Inherited -> Unknown.
    public ExplanationService(RuleSet rules, ProgramMatcher programs, LeftoverHeuristic leftovers, AiCache aiCache)
    {
    }

    public Explanation Explain(ScanNode node)
        => new Explanation(
            node.Name, Badge.Unknown, null, "", "", "", "", null, null,
            ExplanationSource.None, 0, null);

    public void Invalidate(ScanNode node)
    {
        ExplanationChanged?.Invoke(node);
    }

    public event Action<ScanNode>? ExplanationChanged;
}
