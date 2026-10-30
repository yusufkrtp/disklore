using DiskLore.Core.Security;
using Xunit;

namespace DiskLore.Tests.Security;

public class SecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disklore-secrets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    private const string Plain = "sk-ant-api03-very-secret-value";

    [Fact]
    public void File_fallback_round_trips_and_removes()
    {
        var store = FileSecretStore.CreatePlain(Path.Combine(_dir, "secrets.json"));
        Assert.False(store.IsSecure);
        Assert.Equal("File (0600)", store.BackendName);
        Assert.Null(store.Get("apikey:openai-api"));

        store.Set("apikey:openai-api", Plain);
        store.Set("apikey:anthropic-api", "other");
        Assert.Equal(Plain, store.Get("apikey:openai-api"));

        // A second instance over the same file sees the data.
        Assert.Equal(Plain, FileSecretStore.CreatePlain(Path.Combine(_dir, "secrets.json")).Get("apikey:openai-api"));

        store.Remove("apikey:openai-api");
        Assert.Null(store.Get("apikey:openai-api"));
        Assert.Equal("other", store.Get("apikey:anthropic-api"));
        store.Remove("apikey:missing");   // no throw
    }

    [Fact]
    public void File_fallback_restricts_permissions_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "secrets.json");
        FileSecretStore.CreatePlain(path).Set("apikey:x", Plain);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Dpapi_round_trips_and_the_file_never_holds_the_plain_text()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "secrets.json");
        var store = FileSecretStore.CreateDpapi(path);
        Assert.True(store.IsSecure);
        Assert.Equal("Windows DPAPI", store.BackendName);

        store.Set("apikey:anthropic-api", Plain);

        Assert.Equal(Plain, store.Get("apikey:anthropic-api"));
        var onDisk = File.ReadAllText(path);
        Assert.DoesNotContain(Plain, onDisk);
        Assert.DoesNotContain("very-secret", onDisk);
        Assert.Contains("apikey:anthropic-api", onDisk);

        Assert.Equal(Plain, FileSecretStore.CreateDpapi(path).Get("apikey:anthropic-api"));
        store.Remove("apikey:anthropic-api");
        Assert.Null(store.Get("apikey:anthropic-api"));
    }

    [Fact]
    public void Dpapi_blob_is_user_bound_and_garbage_reads_as_missing()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "secrets.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{\"apikey:x\":\"bm90IGEgZHBhcGkgYmxvYg==\"}");
        Assert.Null(FileSecretStore.CreateDpapi(path).Get("apikey:x"));
    }

    [Fact]
    public void Factory_returns_a_working_store_for_this_os()
    {
        var store = SecretStore.Create(_dir);
        Assert.False(string.IsNullOrEmpty(store.BackendName));
        if (OperatingSystem.IsWindows()) Assert.Equal("Windows DPAPI", store.BackendName);
    }
}
