using DiskLore.Core.Security;

namespace DiskLore.Core.Ai.Providers;

public static class AiProviders
{
    private static readonly string[] NoModels = [];

    public static IReadOnlyList<AiProviderInfo> All { get; } =
    [
        new("claude-code", "Claude Code (Claude Pro/Max)", AiProviderKind.SubscriptionCli,
            "Uses your Claude subscription through the official Claude Code CLI. Nothing is stored by DiskLore.",
            "sonnet", ["sonnet", "haiku", "opus"],
            false, false, false, null, "https://docs.anthropic.com/en/docs/claude-code"),
        new("codex-cli", "Codex CLI (ChatGPT Plus/Pro)", AiProviderKind.SubscriptionCli,
            "Uses your ChatGPT plan through the official Codex CLI, in a read-only sandbox. Leave the model empty for Codex's own default.",
            null, NoModels,
            false, false, false, null, "https://github.com/openai/codex"),
        new("gemini-cli", "Gemini CLI (Google account)", AiProviderKind.SubscriptionCli,
            "Uses your Google account through the official Gemini CLI. Leave the model empty for Gemini's own default.",
            null, ["gemini-2.5-pro", "gemini-2.5-flash"],
            false, false, false, null, "https://github.com/google-gemini/gemini-cli"),

        new("anthropic-api", "Anthropic API", AiProviderKind.ApiKey,
            "Calls the Anthropic API with your own key. You pay per request on your Anthropic account.",
            "claude-sonnet-5-5", ["claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5-5"],
            true, false, false, null, "https://console.anthropic.com/settings/keys"),
        new("openai-api", "OpenAI API", AiProviderKind.ApiKey,
            "Calls the OpenAI API with your own key. Load the model list and pick one.",
            null, NoModels,
            true, false, false, "https://api.openai.com/v1", "https://platform.openai.com/api-keys"),
        new("gemini-api", "Google Gemini API", AiProviderKind.ApiKey,
            "Calls the Gemini API with your own key, through its OpenAI-compatible endpoint. Load the model list and pick one.",
            null, NoModels,
            true, false, false, "https://generativelanguage.googleapis.com/v1beta/openai", "https://aistudio.google.com/apikey"),
        new("openrouter-api", "OpenRouter", AiProviderKind.ApiKey,
            "One key for many models from many vendors. Load the model list and pick one.",
            null, NoModels,
            true, false, false, "https://openrouter.ai/api/v1", "https://openrouter.ai/keys"),
        new("openai-compatible", "Custom (OpenAI-compatible)", AiProviderKind.ApiKey,
            "Any server that speaks the OpenAI chat completions protocol. Enter its base URL; the key is optional.",
            null, NoModels,
            true, true, true, null, null),

        new("ollama", "Ollama (local)", AiProviderKind.Local,
            "Runs a model on this computer through Ollama. Nothing leaves your machine. Load the model list and pick one.",
            null, NoModels,
            false, false, true, "http://localhost:11434/v1", "https://ollama.com"),
        new("lmstudio", "LM Studio (local)", AiProviderKind.Local,
            "Runs a model on this computer through LM Studio's local server. Nothing leaves your machine. Load the model list and pick one.",
            null, NoModels,
            false, false, true, "http://localhost:1234/v1", "https://lmstudio.ai"),
    ];

    // The display name without its parenthesised note: "Codex CLI (ChatGPT Plus/Pro)" -> "Codex CLI".
    public static string ShortName(AiProviderInfo info)
    {
        var i = info.DisplayName.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? info.DisplayName[..i] : info.DisplayName;
    }

    public static AiProviderInfo Get(string id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException("Unknown AI provider: " + id, nameof(id));

    public static IAiProvider Create(string id, AiProviderSettings settings, ISecretStore secrets)
        => Create(id, settings, secrets, null);

    internal static IAiProvider Create(string id, AiProviderSettings settings, ISecretStore secrets, HttpMessageHandler? handler)
    {
        var info = Get(id);
        return info.Id switch
        {
            "claude-code" => new ClaudeCodeProvider(info, settings),
            "codex-cli" => new CodexCliProvider(info, settings),
            "gemini-cli" => new GeminiCliProvider(info, settings),
            "anthropic-api" => new AnthropicApiProvider(info, settings, secrets, handler),
            _ => new OpenAiCompatibleProvider(info, settings, secrets, handler),
        };
    }

    // The first installed CLI in the order claude-code, codex-cli, gemini-cli; null when none is installed.
    public static string? DetectDefault()
    {
        foreach (var id in new[] { "claude-code", "codex-cli", "gemini-cli" })
        {
            try
            {
                if (Create(id, new AiProviderSettings(null, null), NullSecrets).IsReady) return id;
            }
            catch (Exception)
            {
            }
        }
        return null;
    }

    private static readonly ISecretStore NullSecrets = new EmptySecretStore();

    private sealed class EmptySecretStore : ISecretStore
    {
        public string BackendName => "none";
        public bool IsSecure => false;
        public string? Get(string name) => null;
        public void Set(string name, string value) { }
        public void Remove(string name) { }
    }
}
