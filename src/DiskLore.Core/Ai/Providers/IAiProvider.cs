namespace DiskLore.Core.Ai.Providers;

public enum AiProviderKind { SubscriptionCli, ApiKey, Local }

public sealed record AiProviderInfo(
    string Id, string DisplayName, AiProviderKind Kind, string Description,
    string? DefaultModel,                       // null = the CLI's/server's own default, or the user must pick
    IReadOnlyList<string> SuggestedModels,
    bool NeedsApiKey, bool ApiKeyOptional, bool NeedsBaseUrl, string? DefaultBaseUrl,
    string? SetupUrl);                          // install / get-a-key page

public sealed record AiProviderSettings(string? Model, string? BaseUrl);

public sealed record AiTestResult(bool Ok, string Message, TimeSpan Elapsed);

public interface IAiProvider
{
    AiProviderInfo Info { get; }
    bool IsReady { get; }                       // CLI found / key stored / model + base URL set
    string StatusText { get; }                  // "Found: C:\...\claude.exe", "Not installed", "No API key saved", ...
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);   // raw model text
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);  // GET /models for HTTP providers; SuggestedModels for CLIs
    Task<AiTestResult> TestAsync(CancellationToken ct);                 // tiny prompt, reports latency or the error
}
