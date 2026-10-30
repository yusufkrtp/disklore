using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DiskLore.Core.Security;

namespace DiskLore.Core.Ai.Providers;

// Shared plumbing of the providers that talk HTTP: key lookup, readiness, timeouts and error mapping.
// A key never appears in a message; messages are written from the status code and the server's error text only.
public abstract class HttpProviderBase : ProviderBase
{
    internal static readonly TimeSpan CloudTimeout = TimeSpan.FromSeconds(120);
    internal static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(300);

    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan });

    private readonly ISecretStore _secrets;
    private readonly Lazy<string?> _apiKey;
    private readonly HttpClient _http;

    protected HttpProviderBase(AiProviderInfo info, AiProviderSettings settings, ISecretStore secrets, HttpMessageHandler? handler)
        : base(info)
    {
        _secrets = secrets;
        _http = handler == null ? SharedClient.Value : new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        Model = string.IsNullOrWhiteSpace(settings.Model) ? info.DefaultModel : settings.Model.Trim();
        BaseUrl = (string.IsNullOrWhiteSpace(settings.BaseUrl) ? info.DefaultBaseUrl : settings.BaseUrl.Trim())?.TrimEnd('/');
        _apiKey = new Lazy<string?>(() =>
        {
            try
            {
                var k = _secrets.Get(KeyName(info.Id))?.Trim();
                return string.IsNullOrEmpty(k) ? null : k;
            }
            catch (Exception) { return null; }
        });
    }

    public static string KeyName(string providerId) => "apikey:" + providerId;

    protected string? Model { get; }
    protected string? BaseUrl { get; }
    protected string? ApiKey => _apiKey.Value;
    protected bool IsLocal => Info.Kind == AiProviderKind.Local;
    protected TimeSpan RequestTimeout => IsLocal ? LocalTimeout : CloudTimeout;

    public override bool IsReady => Problem == null;
    public override string StatusText => Problem ?? (ApiKey != null ? "Ready · key saved" : "Ready");

    private string? Problem
    {
        get
        {
            if (Info.NeedsApiKey && !Info.ApiKeyOptional && ApiKey == null) return "No API key saved";
            if (Info.NeedsBaseUrl && string.IsNullOrWhiteSpace(BaseUrl)) return "No base URL set";
            if (string.IsNullOrWhiteSpace(Model)) return IsLocal ? "No model selected — load models and pick one" : "No model selected";
            return null;
        }
    }

    // Sends the request and returns the body of a successful answer; maps every failure to an AiProviderException.
    protected async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return body;
            throw MapStatus(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiProviderException($"{ShortName} did not answer within {RequestTimeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            throw MapConnection(ex);
        }
    }

    private string ShortName => Info.Id switch
    {
        "ollama" => "Ollama",
        "lmstudio" => "LM Studio",
        _ => Info.DisplayName,
    };

    internal AiProviderException MapStatus(HttpStatusCode status, string body)
    {
        var code = (int)status;
        switch (status)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                return new AiProviderException("API key rejected");
            case HttpStatusCode.NotFound:
                return new AiProviderException(IsLocal
                    ? $"Model not found — pull or load it in {ShortName} first"
                    : "Model not found");
            case HttpStatusCode.TooManyRequests:
                return new AiProviderException("Rate limited / quota reached");
        }
        var detail = ErrorDetail(body);
        if (code >= 500) return new AiProviderException($"{ShortName} server error ({code}){(detail.Length > 0 ? ": " + detail : "")}");
        return new AiProviderException($"{ShortName} returned {code}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    internal AiProviderException MapConnection(HttpRequestException ex)
    {
        var refused = ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
                      || ex.HttpRequestError == HttpRequestError.ConnectionError;
        if (IsLocal && refused) return new AiProviderException($"{ShortName} is not running at {BaseUrl}");
        var where = Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u) ? u.Host : "the server";
        return new AiProviderException($"Can't reach {where}");
    }

    // {"error":{"message":"..."}} (OpenAI, Anthropic) or {"error":"..."} (Ollama); empty when not recognisable.
    internal static string ErrorDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e))
            {
                var text = e.ValueKind switch
                {
                    JsonValueKind.String => e.GetString(),
                    JsonValueKind.Object when e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String => m.GetString(),
                    _ => null,
                };
                text = text?.Trim().ReplaceLineEndings(" ") ?? "";
                return text.Length > 140 ? text[..139] + "…" : text;
            }
        }
        catch (JsonException)
        {
        }
        return "";
    }
}
