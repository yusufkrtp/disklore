using System.Diagnostics;

namespace DiskLore.Core.Ai.Providers;

// Thrown for failures whose message is already written for the user (shown as is in the status bar and Settings).
public sealed class AiProviderException(string message) : Exception(message);

public abstract class ProviderBase(AiProviderInfo info) : IAiProvider
{
    internal const string TestSystemPrompt = "You are a connectivity check. Answer as briefly as possible.";
    internal const string TestUserPrompt = "Reply with the single word OK.";

    public AiProviderInfo Info { get; } = info;
    public abstract bool IsReady { get; }
    public abstract string StatusText { get; }
    public abstract Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);

    public virtual Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        => Task.FromResult(Info.SuggestedModels);

    public async Task<AiTestResult> TestAsync(CancellationToken ct)
    {
        if (!IsReady) return new AiTestResult(false, StatusText, TimeSpan.Zero);
        var sw = Stopwatch.StartNew();
        try
        {
            var reply = await CompleteAsync(TestSystemPrompt, TestUserPrompt, ct).ConfigureAwait(false);
            sw.Stop();
            return new AiTestResult(true, $"OK · {sw.Elapsed.TotalSeconds:0.0} s · {Shorten(reply)}", sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AiTestResult(false, ErrorText(ex), sw.Elapsed);
        }
    }

    public static string ErrorText(Exception ex)
    {
        var text = ex.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return text.Length == 0 ? ex.GetType().Name : text;
    }

    internal static string Shorten(string reply)
    {
        var t = (reply ?? "").Trim().ReplaceLineEndings(" ");
        if (t.Length == 0) return "(empty reply)";
        return t.Length > 60 ? t[..59] + "…" : t;
    }
}
