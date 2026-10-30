namespace DiskLore.App.Services;

// Persisted to <AppData>/settings.json.
public sealed class SettingsStore
{
    public string ThemeId { get; set; } = "Dark";
    public string AiAutoMode { get; set; } = "Off";
    public long AiThresholdBytes { get; set; } = 1L << 30;
    public string AiModel { get; set; } = "sonnet";
    public int AiMaxCallsPerScan { get; set; } = 8;
    public bool ShowLastChanged { get; set; } = true;
    public bool ShellIntegration { get; set; }

    public static SettingsStore Load(string path) => new SettingsStore();

    public void Save(string path) => throw new NotImplementedException();
}
