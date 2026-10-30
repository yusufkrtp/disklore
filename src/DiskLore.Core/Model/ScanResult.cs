namespace DiskLore.Core.Model;

public sealed class ScanResult
{
    public ScanResult(ScanNode root, DateTime startedUtc, TimeSpan elapsed, IReadOnlyList<string> unreadablePaths, bool isDriveRoot)
    {
        Root = root;
        StartedUtc = startedUtc;
        Elapsed = elapsed;
        UnreadablePaths = unreadablePaths;
        IsDriveRoot = isDriveRoot;
    }

    public ScanNode Root { get; }
    public DateTime StartedUtc { get; }
    public TimeSpan Elapsed { get; }
    public IReadOnlyList<string> UnreadablePaths { get; }
    public bool IsDriveRoot { get; }
}
