using DiskLore.Core.Model;

namespace DiskLore.Core.Snapshots;

public static class SnapshotDiff
{
    // Folders only, |Delta| >= minDelta, sorted by Delta descending. New and removed folders included.
    public static IReadOnlyList<DiffEntry> Compare(Snapshot older, ScanNode currentRoot, long minDelta = 50L * 1024 * 1024)
        => throw new NotImplementedException();
}
