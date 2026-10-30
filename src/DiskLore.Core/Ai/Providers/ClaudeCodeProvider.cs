using System.Text.Json;

namespace DiskLore.Core.Ai.Providers;

// `claude -p`: print mode with a JSON envelope, no tools, no user/project settings, no MCP servers, no session on disk.
public sealed class ClaudeCodeProvider : CliProviderBase
{
    private readonly string _model;

    public ClaudeCodeProvider(AiProviderInfo info, AiProviderSettings settings, string? cliPathOverride = null)
        : base(info, "claude", cliPathOverride)
    {
        _model = string.IsNullOrWhiteSpace(settings.Model) ? info.DefaultModel ?? "sonnet" : settings.Model.Trim();
    }

    protected override IReadOnlyList<string> BuildArguments(string workDir, string systemPrompt) => BuildCliArguments(_model, systemPrompt);

    internal static IReadOnlyList<string> BuildCliArguments(string model, string systemPrompt) =>
    [
        "-p",
        "--output-format", "json",
        "--model", model,
        "--tools", "",
        "--setting-sources", "",
        "--strict-mcp-config",
        "--disable-slash-commands",
        "--no-session-persistence",
        "--system-prompt", systemPrompt,
    ];

    protected override string BuildStdin(string systemPrompt, string userPrompt) => userPrompt;

    private protected override string Interpret(CliSupport.CliResult r) => Unwrap(r.Stdout, r.Stderr, r.ExitCode, Command);

    // The envelope is {"type":"result","is_error":false,"result":"<model text>",...}. Plain text is passed through.
    internal static string Unwrap(string stdout, string stderr, int exitCode, string command)
    {
        var trimmed = stdout.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                var result = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : null;
                var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                if (isError || (exitCode != 0 && result == null))
                {
                    var text = result ?? stderr;
                    if (CliSupport.LooksUnauthenticated(text) || CliSupport.LooksUnauthenticated(stderr))
                        throw new AiProviderException(CliSupport.NotSignedInMessage(command));
                    throw new AiProviderException($"{command} reported an error: {CliSupport.FirstLine(text, stderr)}");
                }
                if (result != null) return result;
            }
            catch (JsonException)
            {
            }
        }
        if (exitCode != 0 && trimmed.Length == 0)
        {
            if (CliSupport.LooksUnauthenticated(stderr)) throw new AiProviderException(CliSupport.NotSignedInMessage(command));
            throw new AiProviderException($"{command} exited with {exitCode}: {CliSupport.FirstLine(stderr, stdout)}");
        }
        return stdout;
    }
}
