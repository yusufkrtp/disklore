using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Programs;

public sealed class LeftoverHeuristic : IExplainer
{
    public LeftoverHeuristic(IReadOnlyList<InstalledProgram> programs)
    {
    }

    public Explanation? Explain(ScanNode node) => throw new NotImplementedException();
}
