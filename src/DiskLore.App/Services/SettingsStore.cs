using System.Text.Json;
using System.Text.Json.Serialization;
using DiskLore.Core.Ai.Providers;

namespace DiskLore.App.Services;

// Persisted to <AppData>/settings.json.
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ThemeId { get; set; } = "system";
    public bool AiAuto { get; set; } = true;
    public long AiThresholdBytes { get; set; } = 1L << 30;
    // Selected provider id; null until first launch decides (see MainViewModel), "none" = AI explanations off.
    public string? AiProviderId { get; set; }
    // Per-provider { model, baseUrl }. API keys never live here; they go to the OS secret store.
    public Dictionary<string, AiProviderSettings> AiProviderOptions { get; set; } = new();
    // Legacy: the Claude model of the first versions. Moved into AiProviderOptions["claude-code"] on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AiModel { get; set; }
    public int AiMaxCallsPerScan { get; set; } = 8;
    public bool ShowLastChanged { get; set; } = true;
    public bool ShellIntegration { get; set; }

    public static SettingsStore Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(path), Json);
                if (s != null)
                {
                    if (string.IsNullOrWhiteSpace(s.ThemeId)) s.ThemeId = "system";
                    if (s.AiMaxCallsPerScan < 0) s.AiMaxCallsPerScan = 8;
                    if (s.AiThresholdBytes < 0) s.AiThresholdBytes = 1L << 30;
                    s.MigrateLegacyAi();
                    return s;
                }
            }
        }
        catch (Exception) { }
        return new SettingsStore();
    }

    // Old files carry AiModel (Claude only). Keep that choice: Claude Code stays the provider with its model.
    internal void MigrateLegacyAi()
    {
        AiProviderOptions = AiProviderOptions == null
            ? new()
            : new Dictionary<string, AiProviderSettings>(AiProviderOptions, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(AiModel)) { AiModel = null; return; }
        if (!AiProviderOptions.ContainsKey("claude-code"))
            AiProviderOptions["claude-code"] = new AiProviderSettings(AiModel.Trim(), null);
        if (string.IsNullOrWhiteSpace(AiProviderId)) AiProviderId = "claude-code";
        AiModel = null;
    }

    public AiProviderSettings GetProviderSettings(string id)
        => AiProviderOptions.TryGetValue(id, out var s) ? s : new AiProviderSettings(null, null);

    public void SetProviderSettings(string id, AiProviderSettings settings) => AiProviderOptions[id] = settings;

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, true);
        }
        catch (Exception) { }
    }
}
