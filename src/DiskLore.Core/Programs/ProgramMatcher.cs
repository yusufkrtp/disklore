using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Programs;

public sealed class ProgramMatcher : IExplainer
{
    public ProgramMatcher(IReadOnlyList<InstalledProgram> programs)
    {
    }

    public InstalledProgram? FindOwner(ScanNode node) => throw new NotImplementedException();

    public Explanation? Explain(ScanNode node) => throw new NotImplementedException();
}
