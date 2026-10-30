using DiskLore.Core.Ai.Providers;

namespace DiskLore.Tests.Ai;

// An IAiProvider whose answers come from a delegate; used by explainer and coordinator tests.
internal sealed class FakeProvider(Func<string, string, CancellationToken, Task<string>>? complete = null, bool ready = true) : IAiProvider
{
    public AiProviderInfo Info { get; } = new("fake", "Fake AI (test)", AiProviderKind.Local, "", null, [], false, false, false, null, null);
    public bool IsReady { get; } = ready;
    public string StatusText => IsReady ? "Ready" : "Not ready";
    public List<(string System, string User)> Calls { get; } = [];

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        Calls.Add((systemPrompt, userPrompt));
        return complete != null ? complete(systemPrompt, userPrompt, ct) : Task.FromResult("[]");
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<AiTestResult> TestAsync(CancellationToken ct) => Task.FromResult(new AiTestResult(true, "OK", TimeSpan.Zero));
}
