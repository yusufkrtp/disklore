using DiskLore.Core.Model;

namespace DiskLore.Core.Scanning;

public static class SubtreeAnalyzer
{
    public static SubtreeStats Analyze(ScanNode node) => throw new NotImplementedException();

    public static IReadOnlyList<NamedSize> ExtensionBreakdown(ScanNode node, int max = 50)
        => throw new NotImplementedException();

    public static IReadOnlyList<ScanNode> LargestFiles(ScanNode root, int count = 100)
        => throw new NotImplementedException();
}
