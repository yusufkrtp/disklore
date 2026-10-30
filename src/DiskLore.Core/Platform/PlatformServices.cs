namespace DiskLore.Core.Platform;

public static class PlatformServices
{
    public static IPlatformServices Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPlatformServices();
        if (OperatingSystem.IsMacOS()) return new MacPlatformServices();
        return new LinuxPlatformServices();
    }
}
