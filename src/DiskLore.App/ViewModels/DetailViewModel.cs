using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskLore.App.Converters;
using DiskLore.App.Views;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Scanning;

namespace DiskLore.App.ViewModels;

public sealed record FactBar(string Name, string SizeText, double Fraction);

public sealed partial class DetailViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private ScanNode? _node;
    private ScanNode? _factsNode;
    private Explanation? _e;
    private CancellationTokenSource? _factsCts;
    private CancellationTokenSource? _lockCts;

    private IReadOnlyList<FactBar> _topChildren = Array.Empty<FactBar>();
    private IReadOnlyList<FactBar> _topExtensions = Array.Empty<FactBar>();
    private string _factsLine = "";
    private string _largestText = "";
    private bool _factsLoading;
    private bool _hasFacts;
    private bool _isCheckingLock;
    private string _lockResult = "";
    private IReadOnlyList<string> _lockProcesses = Array.Empty<string>();

    public DetailViewModel(MainViewModel main)
    {
        _main = main;
        main.SelectedChanged += (_, _) => Refresh();
        main.AskingChanged += (_, _) => OnPropertyChanged(string.Empty);
        main.CurrentChanged += (_, _) => Refresh(true);
    }

    // Re-reads the explanation (and facts when the node changed or sizes changed).
    public void Refresh(bool forceFacts = false)
    {
        var node = _main.Selected;
        var changed = !ReferenceEquals(node, _node);
        _node = node;
        _e = node == null ? null : _main.Explain(node);

        if (changed)
        {
            _lockCts?.Cancel();
            _isCheckingLock = false;
            _lockResult = "";
            _lockProcesses = Array.Empty<string>();
        }
        if (changed || forceFacts) LoadFacts();
        OnPropertyChanged(string.Empty);
    }

    // ---- header ------------------------------------------------------------------------------

    public bool HasSelection => _node != null;
    public string UnknownHint => IsFile
        ? "No built-in rule matches it. Claude can take a look at the name and size and write an explanation."
        : "No built-in rule matches it. The facts below still show what is inside; Claude can take a look and write an explanation.";
    public bool IsUnknownEmpty => _node != null && _e is { Badge: Badge.Unknown, Source: ExplanationSource.None } && !_node.AccessDenied && !_node.IsReparsePoint;
    public bool ShowDetails => _node != null && !IsUnknownEmpty;
    public ScanNode? Node => _node;

    public string Title => _e == null || string.IsNullOrWhiteSpace(_e.Title) ? _node?.Name ?? "" : _e.Title;
    public Badge Badge => _node is { AccessDenied: true } or { IsReparsePoint: true } ? Badge.Unknown : _e?.Badge ?? Badge.Unknown;

    public string SourceTag
    {
        get
        {
            if (_e == null || _node == null) return "";
            return _e.Source switch
            {
                ExplanationSource.Rule => "Rule",
                ExplanationSource.AI => $"AI · {_e.Confidence * 100:0}%",
                ExplanationSource.InstalledProgram => "Installed program",
                ExplanationSource.Heuristic => $"Possible leftover · {_e.Confidence * 100:0}%",
                ExplanationSource.Inherited => "Inherited from " + InheritedFrom(),
                _ => ""
            };
        }
    }
    public bool HasSourceTag => SourceTag.Length > 0;

    private string InheritedFrom()
    {
        for (var p = _node?.Parent; p != null; p = p.Parent)
        {
            var pe = _main.Explain(p);
            if (pe.Source != ExplanationSource.Inherited && pe.Source != ExplanationSource.None)
                return string.IsNullOrWhiteSpace(pe.Title) ? p.Name : pe.Title;
        }
        return "a parent folder";
    }

    public string Owner => _e?.Owner ?? "";
    public bool HasOwner => !string.IsNullOrWhiteSpace(_e?.Owner);
    public string FullPath => _node?.FullPath ?? "";
    // Zero-width spaces after every separator: the path wraps between folders, never inside "C:" or a name.
    public string PathDisplay => BreakablePath(FullPath);
    public bool IsFile => _node is { IsDirectory: false };
    public string TypeText
    {
        get
        {
            if (_node is not { IsDirectory: false }) return "";
            var ext = _node.Extension;
            return string.IsNullOrEmpty(ext) ? "No extension" : (ext.StartsWith('.') ? ext : "." + ext).ToLowerInvariant();
        }
    }
    public string ParentName => _node?.Parent == null ? "" : LastSegment(_node.Parent.Name);
    public string ParentPath => _node?.Parent?.FullPath ?? "";

    internal const char WordJoiner = '\u2060';
    internal const char ZeroWidthSpace = '\u200B';

    internal static string BreakablePath(string path)
    {
        if (path.Length == 0) return path;
        var sb = new System.Text.StringBuilder(path.Length + 16);
        for (var i = 0; i < path.Length; i++)
        {
            var ch = path[i];
            sb.Append(ch);
            // The word joiner keeps "C:" glued to the first separator.
            if (ch == ':' && i + 1 < path.Length && path[i + 1] is '\\' or '/') sb.Append(WordJoiner);
            else if (ch is '\\' or '/') sb.Append(ZeroWidthSpace);
            else if (ch == '-') sb.Append(WordJoiner);   // no break inside names like c--Users-Alice
        }
        return sb.ToString();
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return path;
        var i = trimmed.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? trimmed[(i + 1)..] : trimmed;
    }
    public string SizeText => _node == null ? "" : SizeFormat.Format(_node.Size);
    public string FilesText => _node == null ? "" : _node.IsDirectory ? $"{_node.FileCount:N0}" : "";
    public string FoldersText => _node is { IsDirectory: true } ? $"{_node.FolderCount:N0}" : "";
    public bool IsDirectory => _node?.IsDirectory ?? false;

    public string LastChangedText
    {
        get
        {
            if (_node == null) return "";
            var d = TimeFormat.Date(_node.LastWriteUtc);
            return d.Length == 0 ? "" : $"{d} · {TimeFormat.Ago(_node.LastWriteUtc)}";
        }
    }

    public string NoteText
    {
        get
        {
            if (_node == null) return "";
            if (_node.AccessDenied) return "DiskLore couldn't read this folder, so its size is incomplete. Restart as administrator to include it.";
            if (_node.IsReparsePoint) return "This is a link to " + (_node.LinkTarget ?? "another location") + ". DiskLore doesn't follow links, so it counts as 0 B here.";
            return "";
        }
    }
    public bool HasNote => NoteText.Length > 0;

    // ---- sections ----------------------------------------------------------------------------

    public string WhatItIs => _e?.WhatItIs ?? "";
    public bool HasWhatItIs => !string.IsNullOrWhiteSpace(WhatItIs);
    public string WhyBig => _e?.WhyBig ?? "";
    public bool HasWhyBig => !string.IsNullOrWhiteSpace(WhyBig);
    public string IfDeleted => _e?.IfDeleted ?? "";
    public bool HasIfDeleted => !string.IsNullOrWhiteSpace(IfDeleted);
    public string SaferText => _e?.SaferAlternative ?? "";
    public bool HasSafer => !string.IsNullOrWhiteSpace(SaferText) || HasAction;
    public bool HasSaferText => !string.IsNullOrWhiteSpace(SaferText);
    public bool HasAction => _e?.Action != null;
    public string ActionLabel => _e?.Action?.Label ?? "";

    // ---- live facts --------------------------------------------------------------------------

    public bool ShowFacts => IsDirectory && (FactsLoading || HasFacts);
    public bool FactsLoading => _factsLoading;
    public bool HasFacts => _hasFacts;
    public IReadOnlyList<FactBar> TopChildren => _topChildren;
    public IReadOnlyList<FactBar> TopExtensions => _topExtensions;
    public bool HasTopChildren => _topChildren.Count > 0;
    public bool HasTopExtensions => _topExtensions.Count > 0;
    public string FactsLine => _factsLine;
    public string LargestText => _largestText;
    public bool HasLargest => _largestText.Length > 0;

    private void LoadFacts()
    {
        _factsCts?.Cancel();
        _hasFacts = false;
        _topChildren = _topExtensions = Array.Empty<FactBar>();
        _factsLine = _largestText = "";
        var node = _node;
        _factsNode = node;
        if (node == null || !node.IsDirectory || node.IsReparsePoint || node.AccessDenied)
        {
            _factsLoading = false;
            return;
        }

        var cts = _factsCts = new CancellationTokenSource();
        _factsLoading = true;
        Task.Run(() =>
        {
            try { return SubtreeAnalyzer.Analyze(node); }
            catch (Exception) { return null; }
        }).ContinueWith(t =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested || !ReferenceEquals(_factsNode, node)) return;
                _factsLoading = false;
                var s = t.IsCompletedSuccessfully ? t.Result : null;
                if (s != null)
                {
                    var total = Math.Max(1, node.Size);
                    _topChildren = s.TopChildren.Select(c => new FactBar(c.Name, SizeFormat.Format(c.Size), Math.Clamp((double)c.Size / total, 0, 1))).ToList();
                    _topExtensions = s.TopExtensions.Select(c => new FactBar(c.Name, SizeFormat.Format(c.Size), Math.Clamp((double)c.Size / total, 0, 1))).ToList();
                    _largestText = s.LargestFile == null ? "" : $"{s.LargestFile.Name}  ({SizeFormat.Format(s.LargestFile.Size)})";
                    var parts = new List<string> { $"{s.FileCount:N0} files" };
                    if (s.OldestWriteUtc is { } o && s.OldestWriteUtc.Value.Year >= 1980) parts.Add("oldest change " + TimeFormat.Ago(o));
                    if (s.NewestWriteUtc is { } n && s.NewestWriteUtc.Value.Year >= 1980) parts.Add("newest " + TimeFormat.Ago(n));
                    _factsLine = string.Join(" · ", parts);
                    _hasFacts = true;
                }
                OnPropertyChanged(string.Empty);
            });
        }, TaskScheduler.Default);
    }

    // ---- buttons -----------------------------------------------------------------------------

    public bool CanAsk => _node != null && _main.AiAvailable && !_node.IsReparsePoint;
    public bool IsAsking => _node != null && _main.IsAsking(_node);
    public bool CanAskNow => CanAsk && !IsAsking;
    public string AskLabel => _e?.Source == ExplanationSource.AI ? "Re-ask Claude" : "Ask Claude";
    public bool SupportsLock => _main.Platform.SupportsLockDetection;
    public bool IsCheckingLock => _isCheckingLock;
    public bool CanCheckLock => SupportsLock && !_isCheckingLock && _node != null;
    public string LockResult => _lockResult;
    public bool HasLockResult => _lockResult.Length > 0;
    public IReadOnlyList<string> LockProcesses => _lockProcesses;
    public bool CanDelete => _node?.Parent != null;

    [RelayCommand]
    private void Reveal()
    {
        if (_node == null) return;
        Try(() => _main.Platform.RevealInFileManager(_node.IsDirectory ? _node.FullPath : _node.FullPath));
    }

    [RelayCommand]
    private Task CopyPath() => UiHelpers.CopyTextAsync(FullPath);

    [RelayCommand]
    private async Task AskClaude()
    {
        if (_node == null) return;
        await _main.AskClaudeAsync(_node);
    }

    [RelayCommand]
    private Task Recycle() => _node == null ? Task.CompletedTask : _main.DeleteAsync(_node, false);

    [RelayCommand]
    private Task DeletePermanently() => _node == null ? Task.CompletedTask : _main.DeleteAsync(_node, true);

    [RelayCommand]
    public async Task CheckInUse()
    {
        var node = _node;
        if (node == null || !SupportsLock) return;
        _lockCts?.Cancel();
        var cts = _lockCts = new CancellationTokenSource();
        _isCheckingLock = true;
        _lockResult = "";
        _lockProcesses = Array.Empty<string>();
        OnPropertyChanged(string.Empty);

        string result;
        IReadOnlyList<string> procs = Array.Empty<string>();
        try
        {
            procs = await Task.Run(() => FindLockers(_main.Platform, node));
            result = procs.Count == 0
                ? "Nothing is using these files."
                : procs.Count == 1 ? "1 program is using these files:" : $"{procs.Count} programs are using these files:";
        }
        catch (Exception ex)
        {
            result = "Couldn't check: " + ex.Message;
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(_node, node)) return;
        _isCheckingLock = false;
        _lockResult = result;
        _lockProcesses = procs;
        OnPropertyChanged(string.Empty);
    }

    // What "Check in use" runs: the Restart Manager on the node (or its 2000 largest files), one line per process.
    public static IReadOnlyList<string> FindLockers(IPlatformServices platform, ScanNode node)
    {
        var paths = node.IsDirectory
            ? SubtreeAnalyzer.LargestFiles(node, 2000).Select(f => f.FullPath).ToList()
            : new List<string> { node.FullPath };
        return platform.FindLockingProcesses(paths)
            .GroupBy(p => p.Pid)
            .Select(g => g.First())
            .Select(p => string.IsNullOrWhiteSpace(p.AppName) || p.AppName == p.Name
                ? $"{p.Name} (PID {p.Pid})"
                : $"{p.AppName} · {p.Name} (PID {p.Pid})")
            .ToList();
    }

    [RelayCommand]
    private async Task RunAction()
    {
        var action = _e?.Action;
        if (action == null) return;
        var platform = _main.Platform;
        try
        {
            switch (action.Kind)
            {
                case "command":
                    var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    var canRun = platform.SupportsElevatedCommands;
                    var dlg = new ConfirmCommandDialog(action.Label, action.Value, canRun);
                    var ok = owner != null && await dlg.ShowDialog<bool>(owner);
                    if (ok && canRun) platform.RunCommandElevated(action.Value);
                    break;
                case "settingsUri":
                case "url":
                    platform.OpenUri(action.Value);
                    break;
                case "openPath":
                    platform.RevealInFileManager(Environment.ExpandEnvironmentVariables(action.Value));
                    break;
            }
        }
        catch (Exception ex)
        {
            _main.StatusText = "Couldn't run that action: " + ex.Message;
        }
    }

    private void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { _main.StatusText = ex.Message; }
    }
}
