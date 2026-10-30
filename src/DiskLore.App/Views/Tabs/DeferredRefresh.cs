using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiskLore.App.Views.Tabs;

// Runs a rebuild only while the tab is actually on screen; changes that arrive while it is hidden
// are remembered and applied when the tab is shown again.
internal sealed class DeferredRefresh
{
    private readonly Control _owner;
    private readonly Action _run;
    private bool _dirty;

    public DeferredRefresh(Control owner, Action run)
    {
        _owner = owner;
        _run = run;
        owner.AttachedToVisualTree += (_, _) => Flush();
        owner.SizeChanged += (_, _) => Flush();
    }

    public void Mark()
    {
        _dirty = true;
        if (Dispatcher.UIThread.CheckAccess()) Flush();
        else Dispatcher.UIThread.Post(Flush);
    }

    private void Flush()
    {
        if (!_dirty || !_owner.IsEffectivelyVisible || TopLevel.GetTopLevel(_owner) == null) return;
        _dirty = false;
        _run();
    }
}
