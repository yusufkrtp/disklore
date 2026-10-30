namespace DiskLore.Core.Snapshots;

public sealed class Snapshot
{
    public Snapshot(string rootPath, DateTime takenUtc, IReadOnlyDictionary<string, long> sizes)
    {
        RootPath = rootPath;
        TakenUtc = takenUtc;
        Sizes = sizes;
    }

    public string RootPath { get; }
    public DateTime TakenUtc { get; }
    // Key: path relative to root, "" = root; folders + files >= 50 MB.
    public IReadOnlyDictionary<string, long> Sizes { get; }
}
