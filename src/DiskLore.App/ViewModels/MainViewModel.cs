using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskLore.App.Converters;
using DiskLore.App.Services;
using DiskLore.App.Themes;
using DiskLore.App.Views;
using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Programs;
using DiskLore.Core.Rules;
using DiskLore.Core.Scanning;
using DiskLore.Core.Snapshots;

namespace DiskLore.App.ViewModels;

public enum ScanPhase { Idle, Scanning, Done }

public partial class MainViewModel : ObservableObject, IAppContext
{
    private sealed class LatestProgress : IProgress<ScanProgress>
    {
        public volatile ScanProgress? Value;
        public void Report(ScanProgress value) => Value = value;
    }

    private readonly string _settingsPath;
    private readonly AiCache _aiCache;
    private readonly RuleSet _rules;
    private readonly HashSet<ScanNode> _asking = new();
    private readonly Task<IReadOnlyList<InstalledProgram>> _programsTask;
    private ExplanationService _explanations;
    private AiCoordinator? _coordinator;
    private ScanNode? _selected;
    private ScanResult? _current;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _aiCts;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _scanTimer;
    private Stopwatch _scanWatch = new();
    private LatestProgress _latest = new();
    private string _scanPath = "";

    public MainViewModel()
    {
        Platform = PlatformServices.Create();
        var dir = Platform.AppDataDirectory;
        _settingsPath = Path.Combine(dir, "settings.json");
        Settings = SettingsStore.Load(_settingsPath);
        ThemeManager.Apply(Settings.ThemeId);
        Snapshots = new SnapshotStore(Path.Combine(dir, "snapshots"));
        _aiCache = new AiCache(Path.Combine(dir, "ai-cache.json"));
        try { _rules = RuleSet.Load(Path.Combine(dir, "rules.user.json")); }
        catch (Exception) { _rules = RuleSet.Load(null); }
        _explanations = BuildExplanations(Array.Empty<InstalledProgram>());
        RebuildAi();

        Detail = new DetailViewModel(this);
        RefreshDrives();

        // Installed programs come from the registry; load them off the UI thread.
        _programsTask = Task.Run<IReadOnlyList<InstalledProgram>>(() =>
        {
            try { return Platform.Programs.GetInstalled(); }
            catch (Exception) { return Array.Empty<InstalledProgram>(); }
        });
        _programsTask.ContinueWith(t => Dispatcher.UIThread.Post(() => OnProgramsLoaded(t.Result)),
            TaskScheduler.Default);
    }

    // ---- services ----------------------------------------------------------------------------

    public SettingsStore Settings { get; }
    public IPlatformServices Platform { get; }
    public SnapshotStore Snapshots { get; }
    public ExplanationService Explanations => _explanations;
    public TreeModel Tree { get; } = new();
    public DetailViewModel Detail { get; }
    public ObservableCollection<DriveCardViewModel> Drives { get; } = new();
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();

    // Demo and tests can replace explanations; null in normal use.
    internal Func<ScanNode, Explanation>? ExplainOverride { get; set; }

    public Explanation Explain(ScanNode node)
    {
        try { return ExplainOverride?.Invoke(node) ?? _explanations.Explain(node); }
        catch (Exception)
        {
            return new Explanation(node.Name, Badge.Unknown, null, "", "", "", "", null, null, ExplanationSource.None, 0, null);
        }
    }

    public int RuleCount
    {
        get { try { return _rules.Count; } catch (Exception) { return 0; } }
    }

    public int AiCacheCount
    {
        get { try { return _aiCache.Count; } catch (Exception) { return 0; } }
    }

    public void ClearAiCache()
    {
        try { _aiCache.Clear(); } catch (Exception) { }
        Tree.RefreshAllExplanations();
        Detail.Refresh();
    }

    private ExplanationService BuildExplanations(IReadOnlyList<InstalledProgram> programs)
    {
        var svc = new ExplanationService(_rules, new ProgramMatcher(programs), new LeftoverHeuristic(programs), _aiCache);
        svc.ExplanationChanged += OnExplanationChanged;
        return svc;
    }

    private void OnProgramsLoaded(IReadOnlyList<InstalledProgram> programs)
    {
        if (programs.Count == 0) return;
        try
        {
            _explanations.ExplanationChanged -= OnExplanationChanged;
            _explanations = BuildExplanations(programs);
            RebuildAi();
            OnPropertyChanged(nameof(Explanations));
            Tree.RefreshAllExplanations();
            Detail.Refresh();
        }
        catch (Exception) { }
    }

    private void OnExplanationChanged(ScanNode node)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Tree.RefreshExplanation(node);
            foreach (var r in SearchResults) { }
            if (ReferenceEquals(node, _selected)) Detail.Refresh();
        });
    }

    // ---- AI ----------------------------------------------------------------------------------

    [ObservableProperty] private string _aiStatus = "";
    [ObservableProperty] private bool _aiAvailable;

    public void RebuildAi()
    {
        try
        {
            var explainer = new ClaudeCliExplainer(_aiCache, Settings.AiModel);
            _coordinator = new AiCoordinator(explainer, _explanations);
        }
        catch (Exception) { _coordinator = null; }

        var coordinator = _coordinator;
        Task.Run(() =>
        {
            var ok = false;
            try { ok = coordinator?.IsAvailable ?? false; } catch (Exception) { }
            Dispatcher.UIThread.Post(() =>
            {
                AiAvailable = ok;
                Detail.Refresh();
            });
        });
    }

    public bool IsAsking(ScanNode node) => _asking.Contains(node);
    public event EventHandler? AskingChanged;

    public async Task AskClaudeAsync(ScanNode node)
    {
        var coordinator = _coordinator;
        if (coordinator == null || !_asking.Add(node)) return;
        AskingChanged?.Invoke(this, EventArgs.Empty);
        AiStatus = $"Claude is explaining {node.Name}…";
        try
        {
            var status = new Progress<string>(s => { if (!string.IsNullOrWhiteSpace(s)) AiStatus = s; });
            var answered = await Task.Run(() => coordinator.ExplainAsync(new[] { node }, status, CancellationToken.None));
            AiStatus = answered > 0 ? $"Claude explained {node.Name}" : "Claude had no answer for this one";
            Tree.RefreshExplanation(node);
            if (ReferenceEquals(node, _selected)) Detail.Refresh();
        }
        catch (Exception ex)
        {
            AiStatus = "Claude is unavailable: " + ex.Message;
        }
        finally
        {
            _asking.Remove(node);
            AskingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunAutoAiAsync(ScanResult result)
    {
        _aiCts?.Cancel();
        var cts = _aiCts = new CancellationTokenSource();
        var coordinator = _coordinator;
        if (!Settings.AiAuto || coordinator == null) return;
        try
        {
            var available = await Task.Run(() => coordinator.IsAvailable);
            if (!available || cts.IsCancellationRequested) return;
            var threshold = Settings.AiThresholdBytes;
            var maxItems = Math.Max(1, Settings.AiMaxCallsPerScan) * 5;
            var candidates = await Task.Run(() => coordinator.SelectCandidates(result.Root, threshold, maxItems));
            if (candidates.Count == 0 || cts.IsCancellationRequested) return;

            var label = candidates.Count == 1 ? "1 folder" : $"{candidates.Count} folders";
            AiStatus = $"Claude is explaining {label}…";
            var status = new Progress<string>(s => { if (!string.IsNullOrWhiteSpace(s) && !cts.IsCancellationRequested) AiStatus = s; });
            var answered = await Task.Run(() => coordinator.ExplainAsync(candidates, status, cts.Token));
            if (cts.IsCancellationRequested) return;
            AiStatus = answered == 1 ? "Claude explained 1 folder" : $"Claude explained {answered} folders";
            Detail.Refresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!cts.IsCancellationRequested) AiStatus = "";
        }
    }

    // ---- current result ----------------------------------------------------------------------

    public ScanResult? Current => _current;
    public event EventHandler? CurrentChanged;
    protected void RaiseCurrentChanged() => CurrentChanged?.Invoke(this, EventArgs.Empty);

    public bool HasResult => _current != null;
    public string RootPath => _current?.Root.FullPath ?? "";
    public string TotalSizeText => _current == null ? "" : SizeFormat.Format(_current.Root.Size);
    public string FilesText => _current == null ? "" : $"{_current.Root.FileCount:N0} files";
    public string FoldersText => _current == null ? "" : $"{_current.Root.FolderCount:N0} folders";
    public string ElapsedText => _current == null ? "" : "scanned in " + FormatElapsed(_current.Elapsed);
    public int UnreadableCount => _current?.UnreadablePaths.Count ?? 0;
    public bool HasUnreadable => UnreadableCount > 0;
    public string UnreadableText => UnreadableCount == 1 ? "1 folder not readable" : $"{UnreadableCount:N0} folders not readable";
    public bool IsElevated => SafePlatform(() => Platform.IsElevated);
    public bool ShowAdminButton => OperatingSystem.IsWindows() && !IsElevated && HasResult;
    public string RuleCountText => $"{RuleCount:N0} rules";

    private static bool SafePlatform(Func<bool> f)
    {
        try { return f(); } catch (Exception) { return false; }
    }

    public static string FormatElapsed(TimeSpan t)
        => t.TotalSeconds < 60 ? $"{t.TotalSeconds:0.0} s"
         : t.TotalHours < 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
         : $"{(int)t.TotalHours}h {t.Minutes:00}m";

    public void LoadResult(ScanResult result)
    {
        _current = result;
        Tree.Load(result.Root, Explain);
        ClearSearch();
        _selected = null;
        Selected = result.Root;
        Phase = ScanPhase.Done;
        ShowStart = false;
        TabIndex = 0;
        RefreshStatus();
        RaiseCurrentChanged();
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(RootPath));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(FilesText));
        OnPropertyChanged(nameof(FoldersText));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(UnreadableCount));
        OnPropertyChanged(nameof(HasUnreadable));
        OnPropertyChanged(nameof(UnreadableText));
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowAdminButton));
        OnPropertyChanged(nameof(RuleCountText));
        OnPropertyChanged(nameof(CanBackToResults));
        RescanCommand.NotifyCanExecuteChanged();
    }

    // ---- selection / navigation --------------------------------------------------------------

    public ScanNode? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            OnPropertyChanged();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? SelectedChanged;

    // Raised after Navigate so the tree view can select and scroll the row into view.
    public event Action<ScanNode>? RevealRequested;

    [ObservableProperty] private int _tabIndex;
    [ObservableProperty] private bool _showStart = true;

    public void Navigate(ScanNode node)
    {
        ShowStart = false;
        TabIndex = 0;
        Tree.ExpandTo(node);
        Selected = node;
        RevealRequested?.Invoke(node);
    }

    public void ShowInTreemap(ScanNode node)
    {
        Selected = node;
        TabIndex = 3;
    }

    public async Task<bool> DeleteAsync(ScanNode node, bool permanent)
    {
        var owner = MainWindow;
        if (owner == null || node.Parent == null) return false;
        var parent = node.Parent;
        var size = node.Size;
        var selectedInside = false;
        for (var n = _selected; n != null; n = n.Parent)
            if (ReferenceEquals(n, node)) { selectedInside = true; break; }

        bool ok;
        try
        {
            ok = await DeleteFlow.RunAsync(owner, this, node, Explain(node), permanent);
        }
        catch (NotImplementedException)
        {
            StatusText = "Deleting isn't available in this build yet";
            return false;
        }
        catch (Exception ex)
        {
            StatusText = "Delete failed: " + ex.Message;
            return false;
        }
        if (!ok) return false;

        if (selectedInside) Selected = parent;
        Tree.RefreshAfterChange();
        RefreshStatus();
        RaiseCurrentChanged();
        StatusText = $"Deleted {node.Name} ({SizeFormat.Format(size)})";
        RevealRequested?.Invoke(_selected ?? parent);
        return true;
    }

    // ---- scan --------------------------------------------------------------------------------

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsScanning))] private ScanPhase _phase;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _scanError = "";
    [ObservableProperty] private string _scanTitle = "";
    [ObservableProperty] private string _scanFilesText = "0";
    [ObservableProperty] private string _scanFoldersText = "0";
    [ObservableProperty] private string _scanBytesText = "0 B";
    [ObservableProperty] private string _scanPathText = "";
    [ObservableProperty] private string _scanElapsedText = "0.0 s";

    public bool IsScanning => Phase == ScanPhase.Scanning;
    public bool CanBackToResults => HasResult && ShowStart && !IsScanning;

    partial void OnShowStartChanged(bool value) => OnPropertyChanged(nameof(CanBackToResults));

    partial void OnPhaseChanged(ScanPhase value)
    {
        OnPropertyChanged(nameof(CanBackToResults));
        RescanCommand.NotifyCanExecuteChanged();
    }

    public void RefreshDrives()
    {
        Drives.Clear();
        try
        {
            foreach (var d in Platform.GetDrives())
                Drives.Add(new DriveCardViewModel(d, drive => _ = StartScanAsync(drive.RootPath)));
        }
        catch (Exception) { }
    }

    public void Start()
    {
        var path = App.StartupPath;
        if (!string.IsNullOrWhiteSpace(path))
            _ = StartScanAsync(path);
    }

    public async Task StartScanAsync(string path)
    {
        _scanCts?.Cancel();
        _aiCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();

        try { path = Path.GetFullPath(path); } catch (Exception) { }
        _scanPath = path;
        if (!Directory.Exists(path))
        {
            ScanError = $"Can't scan \"{path}\": it isn't a folder.";
            ShowStart = true;
            return;
        }

        ScanError = "";
        AiStatus = "";
        StatusText = "";
        ScanTitle = "Scanning " + path;
        ScanFilesText = "0";
        ScanFoldersText = "0";
        ScanBytesText = "0 B";
        ScanPathText = "";
        ScanElapsedText = "0.0 s";
        Phase = ScanPhase.Scanning;
        ShowStart = true;

        _latest = new LatestProgress();
        var progress = _latest;
        _scanWatch = Stopwatch.StartNew();
        StartScanTimer();

        try
        {
            try { await _programsTask; } catch (Exception) { }
            await Task.Yield();
            var scanner = new Scanner();
            var result = await Task.Run(() => scanner.ScanAsync(path, progress, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;

            StopScanTimer();
            LoadResult(result);
            SaveSnapshotInBackground(result);
            _ = RunAutoAiAsync(result);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_scanCts, cts))
            {
                Phase = HasResult ? ScanPhase.Done : ScanPhase.Idle;
                StatusText = "Scan cancelled";
            }
        }
        catch (NotImplementedException)
        {
            ScanError = "Scanning isn't available in this build yet.";
            Phase = HasResult ? ScanPhase.Done : ScanPhase.Idle;
        }
        catch (Exception ex)
        {
            ScanError = "The scan failed: " + ex.Message;
            Phase = HasResult ? ScanPhase.Done : ScanPhase.Idle;
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) StopScanTimer();
        }
    }

    private void SaveSnapshotInBackground(ScanResult result)
    {
        if (!result.IsDriveRoot) return;
        Task.Run(() =>
        {
            try { Snapshots.Save(result); } catch (Exception) { }
        });
    }

    private void StartScanTimer()
    {
        _scanTimer?.Stop();
        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _scanTimer.Tick += (_, _) => UpdateScanProgress();
        _scanTimer.Start();
    }

    private void StopScanTimer()
    {
        _scanTimer?.Stop();
        _scanTimer = null;
    }

    private void UpdateScanProgress()
    {
        var p = _latest.Value;
        ScanElapsedText = FormatElapsed(_scanWatch.Elapsed);
        if (p == null) return;
        ScanFilesText = p.FilesScanned.ToString("N0");
        ScanFoldersText = p.FoldersScanned.ToString("N0");
        ScanBytesText = SizeFormat.Format(p.BytesScanned);
        ScanPathText = TextTrim.Middle(p.CurrentPath ?? "", 90);
    }

    // ---- commands ----------------------------------------------------------------------------

    private static Window? MainWindow
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        StopScanTimer();
        if (Phase == ScanPhase.Scanning)
        {
            Phase = HasResult ? ScanPhase.Done : ScanPhase.Idle;
            StatusText = "Scan cancelled";
        }
    }

    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        var window = MainWindow;
        if (window == null) return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to scan",
            AllowMultiple = false
        });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path)) await StartScanAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanAsync()
        => StartScanAsync(_current?.Root.FullPath ?? _scanPath);

    private bool CanRescan() => HasResult && !IsScanning;

    [RelayCommand]
    private void Home()
    {
        RefreshDrives();
        ShowStart = true;
    }

    [RelayCommand]
    private void BackToResults()
    {
        if (HasResult) ShowStart = false;
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            Platform.RestartElevated(_current?.Root.FullPath ?? _scanPath);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Administrator restart was cancelled";
        }
        catch (Exception ex)
        {
            StatusText = "Couldn't restart as administrator: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var window = MainWindow;
        if (window == null) return;
        var dlg = new SettingsWindow(this);
        await dlg.ShowDialog(window);
        RebuildAi();
        OnPropertyChanged(nameof(ShowLastChanged));
    }

    [RelayCommand]
    private async Task ShowUnreadableAsync()
    {
        var window = MainWindow;
        if (window == null || _current == null) return;
        await new UnreadableListWindow(_current.UnreadablePaths).ShowDialog(window);
    }

    public bool ShowLastChanged
    {
        get => Settings.ShowLastChanged;
        set
        {
            if (Settings.ShowLastChanged == value) return;
            Settings.ShowLastChanged = value;
            Settings.Save(_settingsPath);
            OnPropertyChanged();
        }
    }

    public void SaveSettings()
    {
        Settings.Save(_settingsPath);
        OnPropertyChanged(nameof(ShowLastChanged));
    }

    // ---- search ------------------------------------------------------------------------------

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchSummary = "";

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        var root = _current?.Root;
        var query = SearchText.Trim();
        if (root == null || query.Length == 0)
        {
            ClearSearch();
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        IsSearching = true;
        IsSearchOpen = true;
        TabIndex = 0;
        ShowStart = false;
        SearchSummary = "Searching…";
        try
        {
            var found = await Task.Run(() => SearchEngine.Find(root, query, 500, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            SearchResults.Clear();
            foreach (var n in found) SearchResults.Add(new SearchResultViewModel(n, Explain));
            SearchSummary = found.Count == 0
                ? $"Nothing matches “{query}”"
                : found.Count >= 500
                    ? $"Largest 500 matches for “{query}”"
                    : $"{found.Count:N0} match{(found.Count == 1 ? "" : "es")} for “{query}”";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) IsSearching = false;
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        _searchCts?.Cancel();
        SearchText = "";
        IsSearchOpen = false;
        IsSearching = false;
        SearchResults.Clear();
    }

    public void OpenSearchResult(SearchResultViewModel result)
    {
        IsSearchOpen = false;
        Navigate(result.Node);
    }
}
