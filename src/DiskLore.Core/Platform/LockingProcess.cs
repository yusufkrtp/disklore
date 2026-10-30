namespace DiskLore.Core.Platform;

public sealed record LockingProcess(int Pid, string Name, string? AppName);
