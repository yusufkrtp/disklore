using DiskLore.Core.Model;

namespace DiskLore.Core.Ai;

public sealed record AiRequestItem(
    string Path,                 // user profile replaced with %USERPROFILE% / ~
    long Size, long FileCount,
    IReadOnlyList<NamedSize> TopChildren,
    IReadOnlyList<NamedSize> TopExtensions,
    IReadOnlyList<string> SampleNames,   // up to 30 file/folder names, no contents
    string? InstalledProgramMatch,
    string? ParentTitle);
