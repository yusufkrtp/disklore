using System.Text.Json;

namespace DiskLore.Core.Ai.Providers;

// Gemini CLI headless mode: the whole prompt on stdin, `-p` marks the run as non-interactive, `--output-format json`
// wraps the answer as {"response": "...", "stats": {...}}. Output handling is tolerant: anything that is not that
// envelope is treated as the answer itself.
public sealed class GeminiCliProvider : CliProviderBase
{
    private readonly string? _model;

    public GeminiCliProvider(AiProviderInfo info, AiProviderSettings settings, string? cliPathOverride = null)
        : base(info, "gemini", cliPathOverride)
    {
        _model = string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim();
    }

    protected override IReadOnlyList<string> BuildArguments(string workDir, string systemPrompt) => BuildCliArguments(_model);

    internal static IReadOnlyList<string> BuildCliArguments(string? model)
    {
        var args = new List<string>
        {
            "-p", "Follow the instructions given on stdin and reply with the answer only.",
            "--output-format", "json",
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-m");
            args.Add(model);
        }
        return args;
    }

    protected override string BuildStdin(string systemPrompt, string userPrompt) => systemPrompt + "\n\n" + userPrompt;

    private protected override string Interpret(CliSupport.CliResult r) => Unwrap(r.Stdout, r.Stderr, r.ExitCode, Command);

    internal static string Unwrap(string stdout, string stderr, int exitCode, string command)
    {
        var trimmed = stdout.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                {
                    var message = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                    if (CliSupport.LooksUnauthenticated(message)) throw new AiProviderException(CliSupport.NotSignedInMessage(command));
                    throw new AiProviderException($"{command} reported an error: {CliSupport.FirstLine(message, stderr)}");
                }
                if (root.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.String)
                    return resp.GetString() ?? "";
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
