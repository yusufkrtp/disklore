using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.App.Services;
using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Programs;
using DiskLore.Core.Rules;
using DiskLore.Core.Snapshots;

namespace DiskLore.App.ViewModels;

public partial class MainViewModel : ObservableObject, IAppContext
{
    private ScanNode? _selected;

    public MainViewModel()
    {
        Platform = PlatformServices.Create();
        var dir = Platform.AppDataDirectory;
        Snapshots = new SnapshotStore(Path.Combine(dir, "snapshots"));
        var programs = Array.Empty<InstalledProgram>();
        Explanations = new ExplanationService(
            RuleSet.Load(Path.Combine(dir, "rules.user.json")),
            new ProgramMatcher(programs),
            new LeftoverHeuristic(programs),
            new AiCache(Path.Combine(dir, "ai-cache.json")));
    }

    public ScanResult? Current { get; private set; }
    public event EventHandler? CurrentChanged;

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

    public ExplanationService Explanations { get; }
    public IPlatformServices Platform { get; }
    public SnapshotStore Snapshots { get; }

    public void Navigate(ScanNode node) => throw new NotImplementedException();

    public Task<bool> DeleteAsync(ScanNode node, bool permanent) => Task.FromResult(false);

    public Task AskClaudeAsync(ScanNode node) => Task.CompletedTask;

    protected void RaiseCurrentChanged() => CurrentChanged?.Invoke(this, EventArgs.Empty);
}
