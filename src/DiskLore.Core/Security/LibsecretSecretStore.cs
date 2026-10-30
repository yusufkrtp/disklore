namespace DiskLore.Core.Security;

// Secret Service (GNOME Keyring, KWallet) through `secret-tool`. The value travels on stdin, never on the command line.
internal sealed class LibsecretSecretStore : ISecretStore
{
    private const string Service = "DiskLore";

    public string BackendName => "libsecret";
    public bool IsSecure => true;

    public string? Get(string name)
    {
        var (code, output) = CommandLine.Run("secret-tool", ["lookup", "service", Service, "account", name]);
        if (code != 0) return null;
        var value = output.TrimEnd('\r', '\n');
        return value.Length == 0 ? null : value;
    }

    public void Set(string name, string value)
    {
        var (code, _) = CommandLine.Run("secret-tool",
            ["store", "--label=DiskLore " + name, "service", Service, "account", name], stdin: value);
        if (code != 0) throw new InvalidOperationException("The system keyring refused to store the key.");
    }

    public void Remove(string name)
        => CommandLine.Run("secret-tool", ["clear", "service", Service, "account", name]);
}
