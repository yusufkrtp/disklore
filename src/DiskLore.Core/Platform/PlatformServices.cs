namespace DiskLore.Core.Platform;

public static class PlatformServices
{
    public static IPlatformServices Create() => new StubPlatformServices();
}
