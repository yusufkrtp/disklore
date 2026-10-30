using System.Text;
using System.Text.Json;
using DiskLore.Core.Ai.Providers;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.Core.Ai;

// Provider-independent: builds the prompt, asks the provider in batches, parses and clamps the answers, fills the cache.
public sealed class AiExplainer
{
    internal const int BatchSize = 5;

    internal const string SystemPrompt =
        "You are a careful disk usage analyst. You never use tools. You reply with JSON only.";

    private readonly AiCache _cache;
    private readonly IAiProvider _provider;

    public AiExplainer(AiCache cache, IAiProvider provider)
    {
        _cache = cache;
        _provider = provider;
    }

    public bool IsAvailable => _provider.IsReady;
    public string ProviderName => _provider.Info.DisplayName;
    // "Codex CLI" instead of "Codex CLI (ChatGPT Plus/Pro)": the form used in status texts.
    public string ProviderShortName => AiProviders.ShortName(_provider.Info);

    // The message of the most recent failed batch, cleared by the next successful one. For the status bar.
    public string? LastError { get; private set; }

    // Batches of up to 5 items per provider call. Returns full path -> explanation and writes every answer to the cache.
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
                var output = await _provider.CompleteAsync(SystemPrompt, prompt, ct).ConfigureAwait(false);
                var expected = batch.Select(b => (b.Item.Path, b.Node.FullPath)).ToList();
                var parsed = AiResponseParser.Parse(output, expected);
                if (parsed.Count == 0) throw new InvalidOperationException("The model's answer could not be read");
                foreach (var (path, explanation) in parsed)
                {
                    _cache.Set(path, explanation);
                    all[path] = explanation;
                }
                LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One failing batch (timeout, provider error, bad answer) does not stop the others.
                LastError = ex.Message.Split((char)10, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
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
}
