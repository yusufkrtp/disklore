namespace DiskLore.Core.Platform;

public sealed record DriveEntry(string RootPath, string Label, string Format, long TotalBytes, long FreeBytes);
