using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Ai;

public sealed class ClaudeCliExplainer
{
    internal const int BatchSize = 5;
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(180);

    private const string SystemPrompt =
        "You are a careful disk usage analyst. You never use tools. You reply with JSON only.";

    private readonly AiCache _cache;
    private readonly string _model;
    private readonly Func<string, CancellationToken, Task<string>>? _runner;
    private readonly Lazy<string?> _cliPath = new(FindCli);

    public ClaudeCliExplainer(AiCache cache, string model = "sonnet")
    {
        _cache = cache;
        _model = string.IsNullOrWhiteSpace(model) ? "sonnet" : model;
    }

    // Test seam: replaces the CLI call. Receives the prompt, returns the raw CLI stdout.
    internal ClaudeCliExplainer(AiCache cache, Func<string, CancellationToken, Task<string>> runner, string model = "sonnet")
        : this(cache, model)
    {
        _runner = runner;
    }

    // True when the claude CLI is found on PATH (cached).
    public bool IsAvailable => _runner != null || _cliPath.Value != null;

    // Batches of up to 5 items per CLI call. Returns full path -> explanation and writes every answer to the cache.
    public async Task<IReadOnlyDictionary<string, Explanation>> ExplainAsync(
        IReadOnlyList<(ScanNode Node, AiRequestItem Item)> items, CancellationToken ct)
    {
        var all = new Dictionary<string, Explanation>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = items.Skip(i).Take(BatchSize).ToList();
            try
            {
                var prompt = BuildPrompt(batch.Select(b => b.Item).ToList());
                var output = _runner != null ? await _runner(prompt, ct).ConfigureAwait(false) : await RunCliAsync(prompt, ct).ConfigureAwait(false);
                var expected = batch.Select(b => (b.Item.Path, b.Node.FullPath)).ToList();
                var parsed = AiResponseParser.Parse(output, expected);
                foreach (var (path, explanation) in parsed)
                {
                    _cache.Set(path, explanation);
                    all[path] = explanation;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // One failing batch (timeout, CLI error, bad answer) does not stop the others.
            }
        }
        return all;
    }

    private static readonly JsonSerializerOptions PromptJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string BuildPrompt(IReadOnlyList<AiRequestItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DiskLore is a disk usage explorer that explains every folder and file it finds, so the user can decide what to delete.");
        sb.AppendLine("For each item below, tell the user what it is, why it is big, whether the operating system needs it and whether deleting it is safe.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Be conservative. If you are not sure, say \"not sure\" in the summary and use a low confidence instead of guessing.");
        sb.AppendLine("- If the folder looks like personal files (documents, photos, projects, downloads), use badge \"user\".");
        sb.AppendLine("- Only the file and folder names and sizes are given. You cannot see file contents.");
        sb.AppendLine("- Keep summary under 90 characters, one line, plain words.");
        sb.AppendLine();
        sb.AppendLine("Badge vocabulary (use exactly one per item):");
        sb.AppendLine("- app: belongs to an installed application or game");
        sb.AppendLine("- check: could be removed but the user should look first");
        sb.AppendLine("- safe: caches, temp or log data that is recreated automatically");
        sb.AppendLine("- leftover: remains of an application that seems to be gone");
        sb.AppendLine("- user: the user's own files");
        sb.AppendLine("- unknown: you cannot tell");
        sb.AppendLine();
        sb.AppendLine("Items (JSON):");
        sb.AppendLine(JsonSerializer.Serialize(items, PromptJson));
        sb.AppendLine();
        sb.AppendLine("Output schema: a JSON array with one object per item, in the same order:");
        sb.AppendLine("[{\"path\": \"<the item's path exactly as given>\", \"title\": \"short name of what it is\", \"badge\": \"app|check|safe|leftover|user|unknown\", " +
                      "\"owner\": \"app or vendor that owns it, or null\", \"summary\": \"one line, max 90 chars\", \"whatItIs\": \"1-2 sentences\", " +
                      "\"whyBig\": \"1-2 sentences\", \"ifDeleted\": \"what happens if the user deletes it\", \"saferAlternative\": \"a safer way to free space, or null\", " +
                      "\"confidence\": 0.0}]");
        sb.AppendLine("Reply with only the JSON array.");
        return sb.ToString();
    }

    // ---- CLI process ----

    private async Task<string> RunCliAsync(string prompt, CancellationToken ct)
    {
        var cli = _cliPath.Value ?? throw new InvalidOperationException("The claude CLI was not found on PATH.");
        var workDir = Path.Combine(Path.GetTempPath(), "disklore-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = workDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (OperatingSystem.IsWindows() && cli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.ArgumentList.Add("/d");
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(cli);
            }
            else
            {
                psi.FileName = cli;
            }
            foreach (var a in BuildArguments(_model)) psi.ArgumentList.Add(a);

            using var process = new Process { StartInfo = psi };
            process.Start();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKillTree(process);
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException($"claude did not answer within {CallTimeout.TotalSeconds:0} s.");
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
                throw new InvalidOperationException($"claude exited with {process.ExitCode}: {stderr.Trim()}");
            return stdout;
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch (Exception) { }
        }
    }

    // Print mode with a JSON envelope, no tools, no user/project settings, no MCP servers, no session on disk.
    internal static IReadOnlyList<string> BuildArguments(string model) =>
    [
        "-p",
        "--output-format", "json",
        "--model", model,
        "--tools", "",
        "--setting-sources", "",
        "--strict-mcp-config",
        "--disable-slash-commands",
        "--no-session-persistence",
        "--system-prompt", SystemPrompt,
    ];

    private static void TryKillTree(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
    }

    internal static string? FindCli()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"')).ToList();
        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
        foreach (var name in names)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return null;
    }
}
