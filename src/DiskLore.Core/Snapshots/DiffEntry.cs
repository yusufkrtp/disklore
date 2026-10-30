namespace DiskLore.Core.Snapshots;

public sealed record DiffEntry(string RelativePath, long OldSize, long NewSize) { public long Delta => NewSize - OldSize; }
