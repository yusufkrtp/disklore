using DiskLore.Core.Model;

namespace DiskLore.Core.Explanations;

public interface IExplainer
{
    // Synchronous, cheap, no I/O beyond what the explainer cached at construction.
    Explanation? Explain(ScanNode node);
}
