namespace DiskLore.Core.Security;

public interface ISecretStore
{
    string BackendName { get; }                 // "Windows DPAPI", "macOS Keychain", "libsecret", "File (0600)"
    bool IsSecure { get; }                      // false only for the 0600 file fallback
    string? Get(string name);                   // name: "apikey:<providerId>"
    void Set(string name, string value);
    void Remove(string name);
}
