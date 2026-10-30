using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiskLore.Core.Security;

namespace DiskLore.Core.Ai.Providers;

// POST {base}/chat/completions. Serves OpenAI, Gemini's OpenAI-compatible endpoint, OpenRouter, Ollama, LM Studio
// and any custom server that speaks the same protocol.
public sealed class OpenAiCompatibleProvider : HttpProviderBase
{
    internal const string OpenRouterReferer = "https://github.com/OWNER/disklore";
    internal const string OpenRouterTitle = "DiskLore";

    public OpenAiCompatibleProvider(AiProviderInfo info, AiProviderSettings settings, ISecretStore secrets, HttpMessageHandler? handler = null)
        : base(info, settings, secrets, handler)
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
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            temperature = 0.2,
        });
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddHeaders(request);
        return request;
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        if (ApiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        if (Info.Id == "openrouter-api")
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", OpenRouterReferer);
            request.Headers.TryAddWithoutValidation("X-Title", OpenRouterTitle);
        }
    }

    // choices[0].message.content is a string, or (some servers) an array of {type:"text", text}.
    internal static string ParseReply(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString()))
                return content.GetString()!;
            if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) sb.Append(t.GetString());
                if (sb.Length > 0) return sb.ToString();
            }
        }
        throw new AiProviderException("The model returned no text");
    }

    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) throw new AiProviderException("No base URL set");
        if (Info.NeedsApiKey && !Info.ApiKeyOptional && ApiKey == null) throw new AiProviderException("No API key saved");
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/models");
        AddHeaders(request);
        var body = await SendAsync(request, ct).ConfigureAwait(false);
        return ParseModelIds(body);
    }

    // {"data":[{"id":"..."}]}; Gemini prefixes ids with "models/", which the chat endpoint does not need.
    internal static IReadOnlyList<string> ParseModelIds(string body)
    {
        var ids = new List<string>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in data.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } s)
                    ids.Add(s.StartsWith("models/", StringComparison.Ordinal) ? s["models/".Length..] : s);
            }
        }
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return ids;
    }
}
