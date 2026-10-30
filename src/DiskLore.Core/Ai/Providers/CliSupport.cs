using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskLore.Core.Ai.Providers;

// Shared plumbing of the subscription CLI providers: finding the executable, running it in an empty
// temp directory with a timeout, and recognising "not signed in" output.
internal static class CliSupport
{
    internal sealed record CliResult(int ExitCode, string Stdout, string Stderr, string WorkDir);

    // Finds `<name>` on PATH and in the usual install locations (GUI apps often start with a short PATH).
    internal static string? FindCli(string name, IEnumerable<string>? extraDirs = null)
    {
        var dirs = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
            dirs.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => d.Trim('"')));
        dirs.AddRange(UsualInstallDirs());
        if (extraDirs != null) dirs.AddRange(extraDirs);

        var names = OperatingSystem.IsWindows() ? new[] { name + ".exe", name + ".cmd" } : new[] { name };
        foreach (var n in names)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    if (dir.Length == 0) continue;
                    var candidate = Path.Combine(dir, n);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> UsualInstallDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (appData.Length > 0) yield return Path.Combine(appData, "npm");
            if (home.Length > 0) yield return Path.Combine(home, ".local", "bin");
            yield break;
        }
        if (home.Length > 0)
        {
            yield return Path.Combine(home, ".local", "bin");
            yield return Path.Combine(home, ".npm-global", "bin");
            yield return Path.Combine(home, ".claude", "local");
            yield return Path.Combine(home, ".bun", "bin");
        }
        yield return "/usr/local/bin";
        yield return "/opt/homebrew/bin";
    }

    // Runs the CLI with an empty temp working directory. The prompt goes in on stdin.
    // `workDirSetup` may return arguments that refer to files inside the work directory.
    internal static async Task<CliResult> RunAsync(
        string cli, IReadOnlyList<string> args, string stdin, string workDir, TimeSpan timeout, string displayName, CancellationToken ct)
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
        if (OperatingSystem.IsWindows() && (cli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || cli.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // npm shims are batch files: run them through cmd with the /s quoting rule so paths with spaces survive.
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = "/d /s /c \"" + string.Join(' ', new[] { cli }.Concat(args).Select(QuoteForCmd)) + "\"";
        }
        else
        {
            psi.FileName = cli;
            foreach (var a in args) psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            try
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), linked.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The CLI closed stdin early (it failed fast); its output explains why.
            }
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            if (ct.IsCancellationRequested) throw;
            throw new AiProviderException($"{displayName} did not answer within {timeout.TotalSeconds:0} s.");
        }

        return new CliResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), workDir);
    }

    internal static string CreateWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "disklore-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static void DeleteWorkDir(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { }
    }

    private static string QuoteForCmd(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    internal static void TryKillTree(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
    }

    private static readonly Regex NotSignedIn = new(
        @"not (logged|signed) in|please (run /login|log ?in|sign ?in)|/login|login required|authentication (required|failed|error)|not authenticated|unauthori[sz]ed|invalid api key|api key (is )?(missing|invalid)|no (credentials|auth)|sign in with|401",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool LooksUnauthenticated(string? text)
        => !string.IsNullOrWhiteSpace(text) && NotSignedIn.IsMatch(text);

    internal static string NotSignedInMessage(string command)
        => $"Not signed in — run `{command}` once in a terminal to sign in";

    // The most useful line of a failed CLI run, for the error message: an "ERROR: ..." line when there is one
    // (CLIs print a banner first), with the message pulled out of a JSON error body.
    internal static string FirstLine(string? stderr, string? stdout)
    {
        foreach (var text in new[] { stderr, stdout })
        {
            var lines = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var line = lines.FirstOrDefault(l => l.StartsWith("error", StringComparison.OrdinalIgnoreCase)) ?? lines.FirstOrDefault();
            if (line == null) continue;
            line = JsonErrorMessage(line) ?? line;
            return line.Length > 160 ? line[..159] + "…" : line;
        }
        return "no output";
    }

    private static string? JsonErrorMessage(string line)
    {
        var start = line.IndexOf('{');
        if (start < 0) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line[start..]);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
                && m.ValueKind == System.Text.Json.JsonValueKind.String)
                return m.GetString();
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return null;
    }
}
