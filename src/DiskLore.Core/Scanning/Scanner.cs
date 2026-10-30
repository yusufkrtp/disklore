using DiskLore.Core.Model;

namespace DiskLore.Core.Scanning;

public sealed class Scanner : IScanner
{
    public Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct)
        => throw new NotImplementedException();
}
