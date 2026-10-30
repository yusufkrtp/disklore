using DiskLore.Core.Model;

namespace DiskLore.Core.Scanning;

public interface IScanner
{
    Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct);
}
