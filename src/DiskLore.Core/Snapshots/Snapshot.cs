namespace DiskLore.Core.Snapshots;

public sealed class Snapshot
{
    public Snapshot(string rootPath, DateTime takenUtc, IReadOnlyDictionary<string, long> sizes)
        : this(rootPath, takenUtc, sizes, null)
    {
    }

    internal Snapshot(string rootPath, DateTime takenUtc, IReadOnlyDictionary<string, long> sizes, IReadOnlySet<string>? folders)
    {
        RootPath = rootPath;
        TakenUtc = takenUtc;
        Sizes = sizes;
        Folders = folders;
    }

    public string RootPath { get; }
    public DateTime TakenUtc { get; }
    // Key: path relative to root, "" = root; folders + files >= 50 MB.
    public IReadOnlyDictionary<string, long> Sizes { get; }

    // Keys of Sizes that are folders. Null when unknown (a hand-built snapshot): every key may then be a folder.
    internal IReadOnlySet<string>? Folders { get; }
}
