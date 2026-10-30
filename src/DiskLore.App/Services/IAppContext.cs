using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Snapshots;

namespace DiskLore.App.Services;

public interface IAppContext
{
    ScanResult? Current { get; }
    event EventHandler? CurrentChanged;              // new scan loaded or tree changed after delete
    ScanNode? Selected { get; set; }
    event EventHandler? SelectedChanged;
    ExplanationService Explanations { get; }
    IPlatformServices Platform { get; }
    SnapshotStore Snapshots { get; }
    void Navigate(ScanNode node);                    // select + expand path in the tree tab and switch to it
    Task<bool> DeleteAsync(ScanNode node, bool permanent);   // runs DeleteFlow, updates tree, raises CurrentChanged
    Task AskAiAsync(ScanNode node);                  // manual "Ask AI" (opens Settings when no provider is ready)
}
