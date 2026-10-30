using DiskLore.Core.Model;

namespace DiskLore.Core.Snapshots;

public sealed class SnapshotStore
{
    private readonly string _directory;

    public SnapshotStore(string directory)
    {
        _directory = directory;
    }

    // Keeps the newest 10 per root.
    public SnapshotInfo Save(ScanResult result) => throw new NotImplementedException();

    // Newest first.
    public IReadOnlyList<SnapshotInfo> List(string rootPath) => throw new NotImplementedException();

    public Snapshot Load(SnapshotInfo info) => throw new NotImplementedException();
}
