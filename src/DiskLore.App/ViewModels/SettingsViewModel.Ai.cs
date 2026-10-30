using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskLore.Core.Ai.Providers;

namespace DiskLore.App.ViewModels;

// One row of the provider ComboBox: a selectable provider, "Off", or a non-selectable group header.
public sealed record ProviderChoice(string Id, string Name, bool IsHeader)
{
    public bool IsSelectable => !IsHeader;
    public bool IsNormal => !IsHeader;
}

// The "AI explanations" section of Settings: which provider is used and how it is connected.
public sealed partial class SettingsViewModel
{
    private const string Off = "none";

    private ProviderChoice? _choice;
    private IAiProvider? _provider;
    private bool _hasKey;
    private CancellationTokenSource? _busyCts;

    public IReadOnlyList<ProviderChoice> Choices { get; private set; } = Array.Empty<ProviderChoice>();
    public ObservableCollection<string> ModelOptions { get; } = new();

    private void BuildChoices()
    {
        var list = new List<ProviderChoice> { new(Off, "Off", false) };
        void Group(string header, AiProviderKind kind)
        {
            list.Add(new ProviderChoice("", header, true));
            foreach (var p in AiProviders.All.Where(p => p.Kind == kind))
                list.Add(new ProviderChoice(p.Id, p.DisplayName, false));
        }
        Group("Subscription (sign in with the official CLI)", AiProviderKind.SubscriptionCli);
        Group("API key", AiProviderKind.ApiKey);
        Group("Local model", AiProviderKind.Local);
        Choices = list;
        var id = _main.Settings.AiProviderId;
        _choice = list.FirstOrDefault(c => !c.IsHeader && c.Id == id) ?? list[0];
    }

    public ProviderChoice? SelectedChoice
    {
        get => _choice;
        set
        {
            if (value == null || value.IsHeader)
            {
                OnPropertyChanged();   // snap back: headers cannot be chosen
                return;
            }
            if (ReferenceEquals(_choice, value)) return;
            _choice = value;
            _main.Settings.AiProviderId = value.Id;
            _main.SaveSettings();
            ResetTransient();
            RebuildProvider();
            _main.RebuildAi();
            OnPropertyChanged();
        }
    }

    private string ProviderId => _choice?.Id ?? Off;
    private AiProviderInfo? Info => ProviderId == Off ? null : AiProviders.All.FirstOrDefault(p => p.Id == ProviderId);

    private void ResetTransient()
    {
        _busyCts?.Cancel();
        TestText = "";
        ModelsText = "";
        ApiKeyInput = "";
        IsTesting = false;
        IsLoadingModels = false;
    }

    // Recreates the provider from the saved settings and refreshes everything the panel shows.
    private void RebuildProvider()
    {
        _provider = null;
        _hasKey = false;
        var info = Info;
        if (info != null)
        {
            try
            {
                _provider = AiProviders.Create(info.Id, _main.Settings.GetProviderSettings(info.Id), _main.Secrets);
                if (info.NeedsApiKey) _hasKey = !string.IsNullOrEmpty(_main.Secrets.Get(HttpProviderBase.KeyName(info.Id)));
            }
            catch (Exception) { }
        }
        ModelOptions.Clear();
        foreach (var m in info?.SuggestedModels ?? Array.Empty<string>()) ModelOptions.Add(m);
        OnPropertyChanged(string.Empty);
    }

    public bool HasProvider => Info != null;
    public bool IsCli => Info?.Kind == AiProviderKind.SubscriptionCli;
    public bool UsesKey => Info?.NeedsApiKey == true;
    public bool ShowBaseUrl => Info?.NeedsBaseUrl == true;
    public string Description => Info?.Description ?? "AI explanations are off. Unknown folders are left without a note.";

    public string StatusText => _provider?.StatusText ?? "";
    public bool StatusIsReady => _provider?.IsReady == true;
    public bool StatusIsProblem => HasProvider && !StatusIsReady;

    // ---- connect hints ----

    private string CliCommand => ProviderId switch
    {
        "claude-code" => "claude",
        "codex-cli" => "codex",
        "gemini-cli" => "gemini",
        _ => "",
    };

    public string CliHint => $"Install it, then run `{CliCommand}` once in a terminal and sign in. DiskLore never sees your login.";
    public string SetupLinkLabel => IsCli ? "Install " + (Info is { } i ? AiProviders.ShortName(i) : "") : UsesKey ? "Get a key" : "Download " + (Info is { } d ? AiProviders.ShortName(d) : "");
    public bool HasSetupUrl => Info?.SetupUrl != null;

    [RelayCommand]
    private void OpenSetupUrl()
    {
        if (Info?.SetupUrl is not { } url) return;
        try { _main.Platform.OpenUri(url); }
        catch (Exception ex) { TestText = ex.Message; }
    }

    // ---- API key ----

    [ObservableProperty] private string _apiKeyInput = "";
    [ObservableProperty] private string _keyMessage = "";

    public bool HasStoredKey => _hasKey;
    public bool CanSaveKey => !string.IsNullOrWhiteSpace(ApiKeyInput);
    public string KeyPlaceholder => _hasKey ? "A key is saved — paste a new one to replace it" : (Info?.ApiKeyOptional == true ? "Optional" : "Paste your API key");
    public bool KeyStorageSecure => _main.Secrets.IsSecure;
    public string KeyStorageNote => _main.Secrets.IsSecure
        ? $"Stored in {_main.Secrets.BackendName}, never in settings or logs."
        : $"No system keyring found: the key is kept in a file only you can read ({_main.Secrets.BackendName}). Install libsecret to store it safely.";

    partial void OnApiKeyInputChanged(string value) => OnPropertyChanged(nameof(CanSaveKey));

    [RelayCommand]
    private void SaveKey()
    {
        var info = Info;
        var key = ApiKeyInput.Trim();
        if (info == null || key.Length == 0) return;
        try
        {
            _main.Secrets.Set(HttpProviderBase.KeyName(info.Id), key);
            KeyMessage = "Key saved.";
        }
        catch (Exception ex)
        {
            KeyMessage = "Could not save the key: " + ex.Message;
            return;
        }
        ApiKeyInput = "";
        TestText = "";
        RebuildProvider();
        _main.RebuildAi();
    }

    [RelayCommand]
    private void RemoveKey()
    {
        var info = Info;
        if (info == null) return;
        try
        {
            _main.Secrets.Remove(HttpProviderBase.KeyName(info.Id));
            KeyMessage = "Key removed.";
        }
        catch (Exception ex)
        {
            KeyMessage = "Could not remove the key: " + ex.Message;
            return;
        }
        TestText = "";
        RebuildProvider();
        _main.RebuildAi();
    }

    // ---- model and base URL ----

    public string Model
    {
        get => _main.Settings.GetProviderSettings(ProviderId).Model ?? "";
        set
        {
            var info = Info;
            if (info == null) return;
            var current = _main.Settings.GetProviderSettings(info.Id);
            var next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (next == current.Model) return;
            _main.Settings.SetProviderSettings(info.Id, current with { Model = next });
            _main.SaveSettings();
            TestText = "";
            RebuildProviderKeepModels();
            _main.RebuildAi();
        }
    }

    public string ModelPlaceholder => Info?.DefaultModel is { } d ? $"Default: {d}" : Info?.Kind == AiProviderKind.SubscriptionCli ? "Default model of the CLI" : "Pick or type a model name";

    public string BaseUrl
    {
        get
        {
            var info = Info;
            return info == null ? "" : _main.Settings.GetProviderSettings(info.Id).BaseUrl ?? info.DefaultBaseUrl ?? "";
        }
        set
        {
            var info = Info;
            if (info == null) return;
            var current = _main.Settings.GetProviderSettings(info.Id);
            var next = string.IsNullOrWhiteSpace(value) || value.Trim() == info.DefaultBaseUrl ? null : value.Trim();
            if (next == current.BaseUrl) return;
            _main.Settings.SetProviderSettings(info.Id, current with { BaseUrl = next });
            _main.SaveSettings();
            TestText = "";
            RebuildProviderKeepModels();
            _main.RebuildAi();
        }
    }

    public string BaseUrlPlaceholder => Info?.DefaultBaseUrl ?? "https://host/v1";

    // Same as RebuildProvider but keeps the model list the user may have loaded.
    private void RebuildProviderKeepModels()
    {
        var keep = ModelOptions.ToList();
        RebuildProvider();
        ModelOptions.Clear();
        foreach (var m in keep) ModelOptions.Add(m);
    }

    [ObservableProperty] private string _modelsText = "";
    [ObservableProperty] private bool _isLoadingModels;
    public bool CanLoadModels => HasProvider && !IsLoadingModels;
    partial void OnIsLoadingModelsChanged(bool value) => OnPropertyChanged(nameof(CanLoadModels));

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        var provider = _provider;
        if (provider == null) return;
        _busyCts?.Cancel();
        var cts = _busyCts = new CancellationTokenSource();
        IsLoadingModels = true;
        ModelsText = "Loading…";
        try
        {
            var models = await provider.ListModelsAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            var current = Model;
            ModelOptions.Clear();
            foreach (var m in models) ModelOptions.Add(m);
            ModelsText = models.Count == 0 ? "The server returned no models." : $"{models.Count} models loaded.";
            OnPropertyChanged(nameof(Model));
            if (current.Length == 0 && Info?.DefaultModel == null && models.Count == 1) Model = models[0];
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) ModelsText = ProviderBase.ErrorText(ex);
        }
        finally
        {
            if (ReferenceEquals(_busyCts, cts)) IsLoadingModels = false;
        }
    }

    // ---- test ----

    [ObservableProperty] private string _testText = "";
    [ObservableProperty] private bool _testOk;
    [ObservableProperty] private bool _isTesting;
    public bool CanTest => HasProvider && !IsTesting;
    public bool HasTestText => TestText.Length > 0;
    public bool TestShowOk => HasTestText && TestOk && !IsTesting;
    public bool TestShowBad => HasTestText && !TestOk && !IsTesting;
    public bool ShowLocalLink => Info is { Kind: AiProviderKind.Local, SetupUrl: not null };
    partial void OnIsTestingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(TestShowOk));
        OnPropertyChanged(nameof(TestShowBad));
    }
    partial void OnTestOkChanged(bool value)
    {
        OnPropertyChanged(nameof(TestShowOk));
        OnPropertyChanged(nameof(TestShowBad));
    }
    partial void OnTestTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasTestText));
        OnPropertyChanged(nameof(TestShowOk));
        OnPropertyChanged(nameof(TestShowBad));
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        var provider = _provider;
        if (provider == null) return;
        _busyCts?.Cancel();
        var cts = _busyCts = new CancellationTokenSource();
        IsTesting = true;
        TestOk = false;
        TestText = "Testing…";
        try
        {
            var result = await provider.TestAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            TestOk = result.Ok;
            TestText = result.Message;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_busyCts, cts)) IsTesting = false;
        }
    }

}
