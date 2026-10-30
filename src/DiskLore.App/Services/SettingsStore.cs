using System.Text.Json;

namespace DiskLore.App.Services;

// Persisted to <AppData>/settings.json.
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ThemeId { get; set; } = "system";
    public bool AiAuto { get; set; } = true;
    public long AiThresholdBytes { get; set; } = 1L << 30;
    public string AiModel { get; set; } = "sonnet";
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
                    return s;
                }
            }
        }
        catch (Exception) { }
        return new SettingsStore();
    }

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
