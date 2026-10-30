namespace DiskLore.Core.Programs;

public sealed record InstalledProgram(
    string DisplayName, string? Publisher, string? InstallLocation, string? Version,
    string? UninstallString, bool IsStoreApp, string? PackageFamilyName);
