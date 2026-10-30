using Avalonia.Controls;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.Services;

public static class DeleteFlow
{
    // In-use check, confirm dialog, platform delete, ScanTree.Remove, deletions.log line.
    // Returns true when the item was deleted.
    public static Task<bool> RunAsync(Window owner, IAppContext ctx, ScanNode node, Explanation e, bool permanent)
        => throw new NotImplementedException();
}
