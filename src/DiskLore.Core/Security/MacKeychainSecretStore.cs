namespace DiskLore.Core.Security;

// macOS Keychain through the `security` tool that ships with the OS.
internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const string Service = "DiskLore";

    public string BackendName => "macOS Keychain";
    public bool IsSecure => true;

    public string? Get(string name)
    {
        var (code, output) = CommandLine.Run("security", ["find-generic-password", "-s", Service, "-a", name, "-w"]);
        if (code != 0) return null;
        var value = output.TrimEnd('\r', '\n');
        return value.Length == 0 ? null : value;
    }

    public void Set(string name, string value)
    {
        var (code, _) = CommandLine.Run("security", ["add-generic-password", "-U", "-s", Service, "-a", name, "-w", value]);
        if (code != 0) throw new InvalidOperationException("The macOS Keychain refused to store the key.");
    }

    public void Remove(string name)
        => CommandLine.Run("security", ["delete-generic-password", "-s", Service, "-a", name]);
}
