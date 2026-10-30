using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Rules;

public sealed class RuleSet : IExplainer
{
    public static RuleSet Load(string? userRulesPath) => new RuleSet();

    public int Count => 0;

    public Explanation? Explain(ScanNode node) => null;
}
