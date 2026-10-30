using DiskLore.Core.Programs;

namespace DiskLore.Core.Platform;

public interface IPlatformServices
{
    // Windows: %LOCALAPPDATA%\DiskLore, Linux: ~/.local/share/DiskLore, macOS: ~/Library/Application Support/DiskLore
    string AppDataDirectory { get; }
    IReadOnlyList<DriveEntry> GetDrives();
    IInstalledProgramProvider Programs { get; }
    bool SupportsLockDetection { get; }
    IReadOnlyList<LockingProcess> FindLockingProcesses(IReadOnlyList<string> filePaths);   // Restart Manager on Windows
    DeleteResult MoveToTrash(string path);
    DeleteResult DeletePermanently(string path);
    void RevealInFileManager(string path);
    void OpenUri(string uri);
    void RunCommandElevated(string commandLine);     // Windows: PowerShell -Verb RunAs -NoExit
    bool SupportsElevatedCommands { get; }
    bool IsElevated { get; }
    void RestartElevated(string? scanPath);          // Windows only
    bool SupportsShellIntegration { get; }
    bool IsShellIntegrationEnabled();
    void SetShellIntegration(bool enabled, string exePath);   // HKCU Directory\shell + Drive\shell
}
