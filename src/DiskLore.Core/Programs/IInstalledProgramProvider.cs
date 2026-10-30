namespace DiskLore.Core.Programs;

public interface IInstalledProgramProvider { IReadOnlyList<InstalledProgram> GetInstalled(); }
