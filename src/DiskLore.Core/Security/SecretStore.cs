namespace DiskLore.Core.Security;

public static class SecretStore
{
    // Picks the best backend of the current OS. Never throws: the worst case is the 0600 file fallback.
    public static ISecretStore Create(string appDataDirectory)
    {
        var file = Path.Combine(appDataDirectory, "secrets.json");
        try
        {
            if (OperatingSystem.IsWindows()) return FileSecretStore.CreateDpapi(file);
            if (OperatingSystem.IsMacOS() && CommandLine.Find("security") != null) return new MacKeychainSecretStore();
            if (OperatingSystem.IsLinux() && CommandLine.Find("secret-tool") != null) return new LibsecretSecretStore();
        }
        catch (Exception)
        {
        }
        return FileSecretStore.CreatePlain(file);
    }
}
