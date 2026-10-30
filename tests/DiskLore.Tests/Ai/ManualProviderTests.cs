using DiskLore.Core.Ai.Providers;
using Xunit;
using Xunit.Abstractions;

namespace DiskLore.Tests.Ai;

// Real calls: they use the account of whoever runs them, so they only run with DISKLORE_MANUAL=1.
// DISKLORE_PROVIDER selects the provider id (claude-code, codex-cli, ollama, ...).
public class ManualProviderTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Manual_test_connection()
    {
        if (Environment.GetEnvironmentVariable("DISKLORE_MANUAL") != "1") return;
        var id = Environment.GetEnvironmentVariable("DISKLORE_PROVIDER") ?? "claude-code";
        var model = Environment.GetEnvironmentVariable("DISKLORE_MODEL");
        var baseUrl = Environment.GetEnvironmentVariable("DISKLORE_BASE_URL");

        var provider = AiProviders.Create(id, new AiProviderSettings(model, baseUrl), new MemorySecrets());
        output.WriteLine($"{id}: ready={provider.IsReady} status={provider.StatusText}");
        var result = await provider.TestAsync(CancellationToken.None);
        output.WriteLine($"ok={result.Ok} elapsed={result.Elapsed.TotalSeconds:0.0}s message={result.Message}");
    }
}
