using System.Text;
using System.Text.Json;
using DiskLore.Core.Security;

namespace DiskLore.Core.Ai.Providers;

// POST https://api.anthropic.com/v1/messages with the user's own API key.
public sealed class AnthropicApiProvider : HttpProviderBase
{
    internal const string Endpoint = "https://api.anthropic.com/v1";
    internal const string Version = "2023-06-01";
    internal const int MaxTokens = 4096;

    public AnthropicApiProvider(AiProviderInfo info, AiProviderSettings settings, ISecretStore secrets, HttpMessageHandler? handler = null)
        : base(info, settings with { BaseUrl = Endpoint }, secrets, handler)
    {
    }

    public override async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        using var request = BuildRequest(systemPrompt, userPrompt);
        var body = await SendAsync(request, ct).ConfigureAwait(false);
        return ParseReply(body);
    }

    internal HttpRequestMessage BuildRequest(string systemPrompt, string userPrompt)
    {
        var json = JsonSerializer.Serialize(new
        {
            model = Model,
            max_tokens = MaxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        });
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddHeaders(request);
        return request;
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("x-api-key", ApiKey ?? "");
        request.Headers.TryAddWithoutValidation("anthropic-version", Version);
    }

    // content is an array of blocks; the text blocks are joined.
    internal static string ParseReply(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
            }
        }
        if (sb.Length == 0) throw new AiProviderException("The model returned no text");
        return sb.ToString();
    }

    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        if (ApiKey == null) throw new AiProviderException("No API key saved");
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + "/models?limit=100");
        AddHeaders(request);
        var body = await SendAsync(request, ct).ConfigureAwait(false);
        return OpenAiCompatibleProvider.ParseModelIds(body);
    }
}
