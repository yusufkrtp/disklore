using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DiskLore.Core.Ai.Providers;
using DiskLore.Core.Security;
using Xunit;

namespace DiskLore.Tests.Ai;

internal sealed class MemorySecrets : ISecretStore
{
    private readonly Dictionary<string, string> _map = new();
    public string BackendName => "Memory";
    public bool IsSecure => true;
    public string? Get(string name) => _map.GetValueOrDefault(name);
    public void Set(string name, string value) => _map[name] = value;
    public void Remove(string name) => _map.Remove(name);
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        return respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class ProviderTests
{
    private const string OpenAiReply = """{"choices":[{"message":{"role":"assistant","content":"OK"}}]}""";
    private const string SecretKey = "sk-test-1234567890";

    private static IAiProvider Make(string id, StubHandler handler, string? model = null, string? baseUrl = null, string? key = null)
    {
        var secrets = new MemorySecrets();
        if (key != null) secrets.Set("apikey:" + id, key);
        return AiProviders.Create(id, new AiProviderSettings(model, baseUrl), secrets, handler);
    }

    // ---- table ----

    [Fact]
    public void Table_has_the_ten_providers_in_three_kinds()
    {
        Assert.Equal(
            ["claude-code", "codex-cli", "gemini-cli", "anthropic-api", "openai-api", "gemini-api", "openrouter-api", "openai-compatible", "ollama", "lmstudio"],
            AiProviders.All.Select(p => p.Id).ToArray());
        Assert.Equal(3, AiProviders.All.Count(p => p.Kind == AiProviderKind.SubscriptionCli));
        Assert.Equal("http://localhost:11434/v1", AiProviders.Get("ollama").DefaultBaseUrl);
        Assert.Equal("http://localhost:1234/v1", AiProviders.Get("lmstudio").DefaultBaseUrl);
        Assert.Equal("claude-sonnet-5-5", AiProviders.Get("anthropic-api").DefaultModel);
        Assert.Equal("sonnet", AiProviders.Get("claude-code").DefaultModel);
        Assert.Null(AiProviders.Get("openai-api").DefaultModel);
        Assert.All(AiProviders.All.Where(p => p.Id != "openai-compatible"), p => Assert.False(string.IsNullOrEmpty(p.SetupUrl)));
        Assert.Throws<ArgumentException>(() => AiProviders.Get("nope"));
        Assert.Equal("Codex CLI", AiProviders.ShortName(AiProviders.Get("codex-cli")));
        _ = AiProviders.DetectDefault();   // must not throw whatever is installed
    }

    // ---- OpenAI-compatible ----

    [Fact]
    public async Task OpenAi_request_shape()
    {
        var handler = new StubHandler(_ => StubHandler.Json(OpenAiReply));
        var p = Make("openai-api", handler, model: "gpt-x", key: SecretKey);

        var text = await p.CompleteAsync("SYS", "USER", CancellationToken.None);

        Assert.Equal("OK", text);
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(SecretKey, handler.Request.Headers.Authorization.Parameter);
        using var doc = JsonDocument.Parse(handler.Body!);
        var root = doc.RootElement;
        Assert.Equal("gpt-x", root.GetProperty("model").GetString());
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble());
        Assert.False(root.TryGetProperty("response_format", out _));
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("SYS", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("USER", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OpenRouter_sends_its_extra_headers()
    {
        var handler = new StubHandler(_ => StubHandler.Json(OpenAiReply));
        var p = Make("openrouter-api", handler, model: "vendor/model", key: SecretKey);
        await p.CompleteAsync("s", "u", CancellationToken.None);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("https://github.com/yusufkrtp/disklore", handler.Request.Headers.GetValues("HTTP-Referer").Single());
        Assert.Equal("DiskLore", handler.Request.Headers.GetValues("X-Title").Single());
    }

    [Fact]
    public async Task Local_provider_needs_no_key_and_sends_no_auth_header()
    {
        var handler = new StubHandler(_ => StubHandler.Json(OpenAiReply));
        var p = Make("ollama", handler, model: "llama3");
        Assert.True(p.IsReady);
        await p.CompleteAsync("s", "u", CancellationToken.None);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Null(handler.Request.Headers.Authorization);
    }

    [Fact]
    public async Task Custom_base_url_is_used_and_trailing_slash_trimmed()
    {
        var handler = new StubHandler(_ => StubHandler.Json(OpenAiReply));
        var p = Make("openai-compatible", handler, model: "m", baseUrl: "https://example.test/v1/");
        Assert.True(p.IsReady);   // key optional
        await p.CompleteAsync("s", "u", CancellationToken.None);
        Assert.Equal("https://example.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public void Readiness_and_status_texts()
    {
        var h = new StubHandler(_ => StubHandler.Json("{}"));
        Assert.Equal("No API key saved", Make("openai-api", h, model: "m").StatusText);
        Assert.False(Make("openai-api", h, model: "m").IsReady);
        Assert.Equal("No model selected", Make("openai-api", h, key: SecretKey).StatusText);
        Assert.Contains("No model selected", Make("ollama", h).StatusText);
        Assert.False(Make("ollama", h).IsReady);
        Assert.Equal("No base URL set", Make("openai-compatible", h, model: "m").StatusText);
        Assert.True(Make("openai-api", h, model: "m", key: SecretKey).IsReady);
        Assert.True(Make("anthropic-api", h, key: SecretKey).IsReady);   // default model
        Assert.False(Make("anthropic-api", h).IsReady);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key rejected")]
    [InlineData(HttpStatusCode.Forbidden, "API key rejected")]
    [InlineData(HttpStatusCode.NotFound, "Model not found")]
    [InlineData(HttpStatusCode.TooManyRequests, "Rate limited / quota reached")]
    public async Task Http_errors_map_to_plain_messages_without_the_key(HttpStatusCode status, string expected)
    {
        foreach (var id in new[] { "openai-api", "anthropic-api" })
        {
            var handler = new StubHandler(_ => StubHandler.Json("{\"error\":{\"message\":\"bad " + SecretKey + "\"}}", status));
            var p = Make(id, handler, model: "m", key: SecretKey);
            var ex = await Assert.ThrowsAsync<AiProviderException>(() => p.CompleteAsync("s", "u", CancellationToken.None));
            Assert.Equal(expected, ex.Message);
            Assert.DoesNotContain(SecretKey, ex.Message);

            var test = await p.TestAsync(CancellationToken.None);
            Assert.False(test.Ok);
            Assert.Equal(expected, test.Message);
        }
    }

    [Fact]
    public async Task Server_error_includes_the_server_text_but_not_the_key()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{\"error\":{\"message\":\"overloaded\"}}", HttpStatusCode.ServiceUnavailable));
        var p = Make("openai-api", handler, model: "m", key: SecretKey);
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => p.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Contains("503", ex.Message);
        Assert.Contains("overloaded", ex.Message);
    }

    [Theory]
    [InlineData("ollama", "Ollama is not running at http://localhost:11434/v1")]
    [InlineData("lmstudio", "LM Studio is not running at http://localhost:1234/v1")]
    public async Task Connection_refused_on_a_local_provider_says_the_app_is_not_running(string id, string expected)
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
        var p = Make(id, handler, model: "m");
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => p.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal(expected, ex.Message);
        Assert.Equal(expected, (await p.TestAsync(CancellationToken.None)).Message);
    }

    [Fact]
    public async Task Connection_failure_on_a_cloud_provider_names_the_host()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route"));
        var p = Make("openai-api", handler, model: "m", key: SecretKey);
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => p.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal("Can't reach api.openai.com", ex.Message);
    }

    [Fact]
    public async Task Test_reports_latency_and_the_reply()
    {
        var p = Make("openai-api", new StubHandler(_ => StubHandler.Json(OpenAiReply)), model: "m", key: SecretKey);
        var r = await p.TestAsync(CancellationToken.None);
        Assert.True(r.Ok);
        Assert.StartsWith("OK · ", r.Message);
        Assert.EndsWith(" s · OK", r.Message);
    }

    [Fact]
    public async Task Test_on_a_provider_that_is_not_ready_reports_the_status()
    {
        var p = Make("openai-api", new StubHandler(_ => StubHandler.Json(OpenAiReply)), model: "m");
        var r = await p.TestAsync(CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Equal("No API key saved", r.Message);
    }

    [Fact]
    public void Openai_reply_parsing_handles_array_content_and_empty_answers()
    {
        Assert.Equal("ab", OpenAiCompatibleProvider.ParseReply("""{"choices":[{"message":{"content":[{"type":"text","text":"a"},{"type":"text","text":"b"}]}}]}"""));
        Assert.Throws<AiProviderException>(() => OpenAiCompatibleProvider.ParseReply("""{"choices":[]}"""));
    }

    [Fact]
    public async Task Models_listing_parses_data_ids_sorted_and_strips_the_gemini_prefix()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"data":[{"id":"models/gemini-b"},{"id":"models/gemini-a"},{"object":"x"}]}"""));
        var p = Make("gemini-api", handler, key: SecretKey);
        var models = await p.ListModelsAsync(CancellationToken.None);
        Assert.Equal(["gemini-a", "gemini-b"], models.ToArray());
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/models", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
    }

    [Fact]
    public async Task Models_listing_of_a_local_server_works_without_a_key()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"data":[{"id":"llama3:8b"},{"id":"qwen2.5"}]}"""));
        var models = await Make("ollama", handler).ListModelsAsync(CancellationToken.None);
        Assert.Equal(["llama3:8b", "qwen2.5"], models.ToArray());
    }

    // ---- Anthropic ----

    [Fact]
    public async Task Anthropic_request_shape_and_reply()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"content":[{"type":"text","text":"Hello "},{"type":"text","text":"there"}]}"""));
        var p = Make("anthropic-api", handler, key: SecretKey);

        var text = await p.CompleteAsync("SYS", "USER", CancellationToken.None);

        Assert.Equal("Hello there", text);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal(SecretKey, handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
        Assert.Null(handler.Request.Headers.Authorization);
        using var doc = JsonDocument.Parse(handler.Body!);
        var root = doc.RootElement;
        Assert.Equal("claude-sonnet-5-5", root.GetProperty("model").GetString());
        Assert.Equal(4096, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("SYS", root.GetProperty("system").GetString());
        var msg = root.GetProperty("messages")[0];
        Assert.Equal("user", msg.GetProperty("role").GetString());
        Assert.Equal("USER", msg.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Anthropic_model_setting_overrides_the_default_and_models_list_parses()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"data":[{"id":"claude-haiku-5-5"},{"id":"claude-opus-5-5"}],"has_more":false}"""));
        var p = Make("anthropic-api", handler, model: "claude-haiku-5-5", key: SecretKey);
        var models = await p.ListModelsAsync(CancellationToken.None);
        Assert.Equal(["claude-haiku-5-5", "claude-opus-5-5"], models.ToArray());
        Assert.StartsWith("https://api.anthropic.com/v1/models", handler.Request!.RequestUri!.ToString());
        Assert.Equal(SecretKey, handler.Request.Headers.GetValues("x-api-key").Single());
    }

    // ---- CLI providers (pure parts) ----

    [Fact]
    public void Claude_arguments_keep_the_locked_down_print_mode()
    {
        var args = ClaudeCodeProvider.BuildCliArguments("haiku", "SYS");
        Assert.Equal(
            ["-p", "--output-format", "json", "--model", "haiku", "--tools", "", "--setting-sources", "", "--strict-mcp-config",
             "--disable-slash-commands", "--no-session-persistence", "--system-prompt", "SYS"],
            args.ToArray());
    }

    [Fact]
    public void Claude_unwrap_returns_the_result_field()
    {
        var envelope = JsonSerializer.Serialize(new { type = "result", is_error = false, result = "[{\"x\":1}]" });
        Assert.Equal("[{\"x\":1}]", ClaudeCodeProvider.Unwrap(envelope, "", 0, "claude"));
        Assert.Equal("plain text", ClaudeCodeProvider.Unwrap("plain text", "", 0, "claude"));
    }

    [Fact]
    public void Claude_unwrap_maps_a_login_problem_and_other_errors()
    {
        var notLoggedIn = JsonSerializer.Serialize(new { type = "result", is_error = true, result = "Invalid API key · Please run /login" });
        var ex = Assert.Throws<AiProviderException>(() => ClaudeCodeProvider.Unwrap(notLoggedIn, "", 1, "claude"));
        Assert.Equal("Not signed in — run `claude` once in a terminal to sign in", ex.Message);

        var other = JsonSerializer.Serialize(new { type = "result", is_error = true, result = "Something broke" });
        Assert.Contains("Something broke", Assert.Throws<AiProviderException>(() => ClaudeCodeProvider.Unwrap(other, "", 1, "claude")).Message);

        var empty = Assert.Throws<AiProviderException>(() => ClaudeCodeProvider.Unwrap("", "Error: not logged in", 1, "claude"));
        Assert.StartsWith("Not signed in", empty.Message);
    }

    [Fact]
    public void Codex_arguments_use_exec_read_only_and_stdin_and_the_model_only_when_set()
    {
        var args = CodexCliProvider.BuildCliArguments(null).ToList();
        Assert.Equal("exec", args[0]);
        Assert.Contains("--skip-git-repo-check", args);
        Assert.Contains("--ephemeral", args);
        Assert.Contains("--ignore-user-config", args);
        Assert.Equal("read-only", args[args.IndexOf("--sandbox") + 1]);
        Assert.Equal("last-message.txt", args[args.IndexOf("--output-last-message") + 1]);
        Assert.DoesNotContain("--model", args);
        Assert.Equal("-", args[^1]);

        var withModel = CodexCliProvider.BuildCliArguments("gpt-5-codex").ToList();
        Assert.Equal("gpt-5-codex", withModel[withModel.IndexOf("--model") + 1]);
        Assert.Equal("-", withModel[^1]);
    }

    [Fact]
    public void Gemini_arguments_and_output_unwrapping()
    {
        var args = GeminiCliProvider.BuildCliArguments(null).ToList();
        Assert.Equal("-p", args[0]);
        Assert.Equal("json", args[args.IndexOf("--output-format") + 1]);
        Assert.DoesNotContain("-m", args);
        var withModel = GeminiCliProvider.BuildCliArguments("gemini-2.5-flash").ToList();
        Assert.Equal("gemini-2.5-flash", withModel[withModel.IndexOf("-m") + 1]);

        Assert.Equal("the answer", GeminiCliProvider.Unwrap("""{"response":"the answer","stats":{}}""", "", 0, "gemini"));
        Assert.Equal("not json at all", GeminiCliProvider.Unwrap("not json at all", "", 0, "gemini"));
        var ex = Assert.Throws<AiProviderException>(() => GeminiCliProvider.Unwrap("""{"error":{"message":"Please sign in first"}}""", "", 1, "gemini"));
        Assert.StartsWith("Not signed in", ex.Message);
    }

    [Fact]
    public async Task Cli_status_matches_whether_the_executable_is_found()
    {
        var path = Path.Combine(Path.GetTempPath(), "fake-claude.exe");
        var found = new ClaudeCodeProvider(AiProviders.Get("claude-code"), new AiProviderSettings(null, null), path);
        Assert.True(found.IsReady);
        Assert.Equal("Found: " + path, found.StatusText);

        var real = new ClaudeCodeProvider(AiProviders.Get("claude-code"), new AiProviderSettings(null, null));
        if (!real.IsReady)
        {
            Assert.Equal("Not installed", real.StatusText);
            var ex = await Assert.ThrowsAsync<AiProviderException>(() => real.CompleteAsync("s", "u", CancellationToken.None));
            Assert.Equal("Not installed", ex.Message);
        }
    }
}
