using DiskLore.Core.Programs;

namespace DiskLore.Core.Platform;

// Placeholder until the Windows and Unix implementations land.
internal sealed class StubPlatformServices : IPlatformServices
{
    private sealed class EmptyPrograms : IInstalledProgramProvider
    {
        public IReadOnlyList<InstalledProgram> GetInstalled() => Array.Empty<InstalledProgram>();
    }

    public StubPlatformServices()
    {
        string baseDir;
        if (OperatingSystem.IsWindows())
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        else if (OperatingSystem.IsMacOS())
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        AppDataDirectory = Path.Combine(baseDir, "DiskLore");
        Directory.CreateDirectory(AppDataDirectory);
    }

    public string AppDataDirectory { get; }

    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                list.Add(new DriveEntry(d.RootDirectory.FullName, d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    public IInstalledProgramProvider Programs { get; } = new EmptyPrograms();
    public bool SupportsLockDetection => false;
    public IReadOnlyList<LockingProcess> FindLockingProcesses(IReadOnlyList<string> filePaths) => Array.Empty<LockingProcess>();
    public DeleteResult MoveToTrash(string path) => new(false, "Not implemented");
    public DeleteResult DeletePermanently(string path) => new(false, "Not implemented");
    public void RevealInFileManager(string path) { }
    public void OpenUri(string uri) { }
    public void RunCommandElevated(string commandLine) { }
    public bool SupportsElevatedCommands => false;
    public bool IsElevated => false;
    public void RestartElevated(string? scanPath) { }
    public bool SupportsShellIntegration => false;
    public bool IsShellIntegrationEnabled() => false;
    public void SetShellIntegration(bool enabled, string exePath) { }
}
