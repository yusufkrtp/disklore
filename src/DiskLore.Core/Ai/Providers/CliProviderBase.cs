namespace DiskLore.Core.Ai.Providers;

// A subscription provider that runs the vendor's own CLI as a child process. DiskLore never sees the credentials.
public abstract class CliProviderBase : ProviderBase
{
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(180);

    private readonly Lazy<string?> _cliPath;

    protected CliProviderBase(AiProviderInfo info, string command, string? cliPathOverride = null) : base(info)
    {
        Command = command;
        _cliPath = new Lazy<string?>(() => cliPathOverride ?? CliSupport.FindCli(command));
    }

    // The executable name the user types in a terminal: claude, codex, gemini.
    protected string Command { get; }
    protected string? CliPath => _cliPath.Value;

    public override bool IsReady => CliPath != null;
    public override string StatusText => CliPath is { } p ? "Found: " + p : "Not installed";

    // Arguments for one call. `workDir` is the empty directory the CLI runs in.
    protected abstract IReadOnlyList<string> BuildArguments(string workDir, string systemPrompt);
    protected abstract string BuildStdin(string systemPrompt, string userPrompt);
    // Turns the finished process into the model text; throws AiProviderException for failures.
    private protected abstract string Interpret(CliSupport.CliResult result);

    public override async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var cli = CliPath ?? throw new AiProviderException("Not installed");
        var workDir = CliSupport.CreateWorkDir();
        try
        {
            var result = await CliSupport.RunAsync(cli, BuildArguments(workDir, systemPrompt), BuildStdin(systemPrompt, userPrompt),
                workDir, CallTimeout, Info.DisplayName, ct).ConfigureAwait(false);
            return Interpret(result);
        }
        finally
        {
            CliSupport.DeleteWorkDir(workDir);
        }
    }

    private protected AiProviderException Failure(CliSupport.CliResult r)
    {
        var combined = r.Stderr + "\n" + r.Stdout;
        if (CliSupport.LooksUnauthenticated(combined)) return new AiProviderException(CliSupport.NotSignedInMessage(Command));
        return new AiProviderException($"{Command} exited with {r.ExitCode}: {CliSupport.FirstLine(r.Stderr, r.Stdout)}");
    }
}
