namespace DiskLore.Core.Ai.Providers;

// `codex exec`: non-interactive, read-only sandbox, no session on disk, prompt on stdin, the final message written to a file.
public sealed class CodexCliProvider : CliProviderBase
{
    internal const string OutputFileName = "last-message.txt";

    private readonly string? _model;

    public CodexCliProvider(AiProviderInfo info, AiProviderSettings settings, string? cliPathOverride = null)
        : base(info, "codex", cliPathOverride)
    {
        _model = string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim();
    }

    // The output file is a relative name: the process runs in the empty temp directory, so no path with spaces
    // has to survive the cmd.exe shim.
    protected override IReadOnlyList<string> BuildArguments(string workDir, string systemPrompt) => BuildCliArguments(_model);

    internal static IReadOnlyList<string> BuildCliArguments(string? model)
    {
        var args = new List<string>
        {
            "exec",
            "--skip-git-repo-check",
            "--ephemeral",
            "--ignore-user-config",   // your hooks, MCP servers and default model must not affect this call; the login is still used
            "--sandbox", "read-only",
            "--color", "never",
            "--output-last-message", OutputFileName,
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("--model");
            args.Add(model);
        }
        args.Add("-");   // read the prompt from stdin
        return args;
    }

    // Codex has no separate system prompt switch for exec: the instructions come first in the prompt.
    protected override string BuildStdin(string systemPrompt, string userPrompt) => systemPrompt + "\n\n" + userPrompt;

    public override async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var cli = CliPath ?? throw new AiProviderException("Not installed");
        var workDir = CliSupport.CreateWorkDir();
        try
        {
            var result = await CliSupport.RunAsync(cli, BuildArguments(workDir, systemPrompt), BuildStdin(systemPrompt, userPrompt),
                workDir, CallTimeout, Info.DisplayName, ct).ConfigureAwait(false);
            var file = Path.Combine(workDir, OutputFileName);
            var last = File.Exists(file) ? File.ReadAllText(file) : null;
            return Interpret(result, last);
        }
        finally
        {
            CliSupport.DeleteWorkDir(workDir);
        }
    }

    private protected override string Interpret(CliSupport.CliResult result) => Interpret(result, null);

    // Prefer the last-message file; fall back to stdout when the file is missing.
    internal string Interpret(CliSupport.CliResult r, string? lastMessage)
    {
        if (!string.IsNullOrWhiteSpace(lastMessage)) return lastMessage;
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Stdout)) throw Failure(r);
        return r.Stdout;
    }
}
