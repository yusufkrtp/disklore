namespace DiskLore.Core.Snapshots;

public sealed record SnapshotInfo(string FilePath, string RootPath, DateTime TakenUtc, long TotalSize);
