using System.Diagnostics;
using System.Text;

namespace DiskLore.Core.Security;

// Runs a small helper process (security, secret-tool) and finds executables on PATH.
internal static class CommandLine
{
    public static string? Find(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    // Returns exit code and stdout. `stdin` is written and closed when not null.
    public static (int ExitCode, string Output) Run(string file, IEnumerable<string> args, string? stdin = null, int timeoutMs = 10000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardInput = stdin != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + file);
        var stdout = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (stdin != null)
        {
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
        }
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch (Exception) { }
            throw new TimeoutException(Path.GetFileName(file) + " did not answer in time.");
        }
        return (p.ExitCode, stdout.GetAwaiter().GetResult());
    }
}
