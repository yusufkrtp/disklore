using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskLore.App.Themes;

namespace DiskLore.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private ThemeOption? _theme;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        Themes = ThemeManager.Options();
        _theme = Themes.FirstOrDefault(t => string.Equals(t.Id, main.Settings.ThemeId, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
        AiAvailable = main.AiAvailable;
    }

    public IReadOnlyList<ThemeOption> Themes { get; }
    public IReadOnlyList<string> Models { get; } = new[] { "sonnet", "haiku", "opus" };

    public ThemeOption? SelectedTheme
    {
        get => _theme;
        set
        {
            if (value == null || ReferenceEquals(_theme, value)) return;
            _theme = value;
            _main.Settings.ThemeId = value.Id;
            _main.SaveSettings();
            ThemeManager.Apply(value.Id);
            OnPropertyChanged();
        }
    }

    public bool AiAuto
    {
        get => _main.Settings.AiAuto;
        set { _main.Settings.AiAuto = value; _main.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal? ThresholdGb
    {
        get => (decimal)_main.Settings.AiThresholdBytes / (1L << 30);
        set
        {
            if (value == null) return;
            _main.Settings.AiThresholdBytes = (long)(Math.Max(0, value.Value) * (1L << 30));
            _main.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string Model
    {
        get => _main.Settings.AiModel;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _main.Settings.AiModel = value;
            _main.SaveSettings();
            OnPropertyChanged();
        }
    }

    public decimal? MaxCalls
    {
        get => _main.Settings.AiMaxCallsPerScan;
        set
        {
            if (value == null) return;
            _main.Settings.AiMaxCallsPerScan = (int)Math.Clamp(value.Value, 0, 100);
            _main.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool AiAvailable { get; }
    public string AiAvailabilityText => AiAvailable
        ? "Claude CLI found. Unknown folders can be explained."
        : "Claude CLI not found on PATH. Install Claude Code to get AI explanations.";

    [ObservableProperty] private string _cacheText = "";

    public string ClearCacheLabel => $"Clear AI cache ({_main.AiCacheCount} answers)";

    [RelayCommand]
    private void ClearCache()
    {
        _main.ClearAiCache();
        OnPropertyChanged(nameof(ClearCacheLabel));
    }

    public bool ShellSupported => SafeBool(() => _main.Platform.SupportsShellIntegration);

    private bool _shellEnabled;
    private bool _shellLoaded;
    public bool ShellEnabled
    {
        get
        {
            if (!_shellLoaded)
            {
                _shellLoaded = true;
                _shellEnabled = ShellSupported && SafeBool(() => _main.Platform.IsShellIntegrationEnabled());
            }
            return _shellEnabled;
        }
        set
        {
            if (value == _shellEnabled) return;
            try
            {
                _main.Platform.SetShellIntegration(value, Environment.ProcessPath ?? "");
                _shellEnabled = value;
                _main.Settings.ShellIntegration = value;
                _main.SaveSettings();
            }
            catch (Exception ex)
            {
                _main.StatusText = "Couldn't change Explorer integration: " + ex.Message;
            }
            OnPropertyChanged();
        }
    }

    public bool ShowLastChanged
    {
        get => _main.ShowLastChanged;
        set { _main.ShowLastChanged = value; OnPropertyChanged(); }
    }

    public string DataFolder => _main.Platform.AppDataDirectory;

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { _main.Platform.RevealInFileManager(DataFolder); }
        catch (Exception ex) { _main.StatusText = ex.Message; }
    }

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch (Exception) { return false; }
    }
}
