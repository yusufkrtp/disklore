namespace DiskLore.Core.Model;

public sealed class SubtreeStats
{
    public SubtreeStats(
        IReadOnlyList<NamedSize> topChildren,
        IReadOnlyList<NamedSize> topExtensions,
        long fileCount,
        NamedSize? largestFile,
        DateTime? oldestWriteUtc,
        DateTime? newestWriteUtc)
    {
        TopChildren = topChildren;
        TopExtensions = topExtensions;
        FileCount = fileCount;
        LargestFile = largestFile;
        OldestWriteUtc = oldestWriteUtc;
        NewestWriteUtc = newestWriteUtc;
    }

    public IReadOnlyList<NamedSize> TopChildren { get; }
    public IReadOnlyList<NamedSize> TopExtensions { get; }
    public long FileCount { get; }
    public NamedSize? LargestFile { get; }
    public DateTime? OldestWriteUtc { get; }
    public DateTime? NewestWriteUtc { get; }
}
