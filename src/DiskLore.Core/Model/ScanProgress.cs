namespace DiskLore.Core.Model;

public sealed record ScanProgress(long FilesScanned, long FoldersScanned, long BytesScanned, string CurrentPath);
