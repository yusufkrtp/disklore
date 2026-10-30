using Avalonia.Controls;
using DiskLore.App.Dialogs;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;
using DiskLore.Core.Scanning;

namespace DiskLore.App.Services;

public static class DeleteFlow
{
    // In-use check, confirm dialog, platform delete, ScanTree.Remove, deletions.log line.
    // Returns true when the item was deleted.
    public static async Task<bool> RunAsync(Window owner, IAppContext ctx, ScanNode node, Explanation e, bool permanent)
    {
        var platform = ctx.Platform;
        var path = node.FullPath;
        var trashName = OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";

        // 1. Never delete a drive, the scan root or the user profile itself.
        if (IsProtected(node, path, ctx))
        {
            await new MessageDialog("Cannot delete this",
                "DiskLore never deletes a whole drive, the scan root or your user profile folder. " +
                "Open it and delete items inside instead.").ShowDialog<bool>(owner);
            return false;
        }

        // 2. Open files.
        if (platform.SupportsLockDetection)
        {
            var locking = await WithProgress(owner, "Checking for open files...", () => FindLocks(platform, node));
            if (locking.Count > 0)
            {
                bool deleteAnyway = await new InUseDialog(locking).ShowDialog<bool>(owner);
                if (!deleteAnyway) return false;
            }
        }

        // 3. Confirm.
        bool confirmed;
        if (permanent && e.Badge is Badge.Critical or Badge.Shrink)
            confirmed = await new CriticalDeleteDialog(node, e).ShowDialog<bool>(owner);
        else
            confirmed = await new ConfirmDeleteDialog(node, e, permanent, trashName).ShowDialog<bool>(owner);
        if (!confirmed) return false;

        // 4. Delete off the UI thread.
        long size = node.Size;
        var result = await WithProgress(owner, permanent ? "Deleting..." : "Moving to " + trashName + "...",
            () => permanent ? platform.DeletePermanently(path) : platform.MoveToTrash(path));

        bool gone = !File.Exists(path) && !Directory.Exists(path);
        if (!result.Success && !gone)
        {
            await new MessageDialog("Some items could not be deleted",
                (result.Error ?? "The delete failed.") +
                "\n\nThe item may be partly deleted. Rescan to see what is left.").ShowDialog<bool>(owner);
            return false;
        }

        // 5. Update the tree and log.
        ScanTree.Remove(node);
        AppendLog(platform.AppDataDirectory, permanent ? "permanent" : "trash", size, e.Badge, path);
        return true;
    }

    private static bool IsProtected(ScanNode node, string path, IAppContext ctx)
    {
        if (node.Parent == null) return true;
        if (ctx.Current != null && ReferenceEquals(ctx.Current.Root, node)) return true;

        var trimmed = path.TrimEnd('\\', '/');
        var root = Path.GetPathRoot(path);
        if (root != null && string.Equals(root.TrimEnd('\\', '/'), trimmed, PathComparison)) return true;
        if (trimmed.Length == 0) return true;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        if (profile.Length > 0 && string.Equals(profile, trimmed, PathComparison)) return true;
        return false;
    }

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static IReadOnlyList<LockingProcess> FindLocks(IPlatformServices platform, ScanNode node)
    {
        try
        {
            var files = new List<string>();
            if (!node.IsDirectory)
            {
                files.Add(node.FullPath);
            }
            else
            {
                IEnumerable<ScanNode> nodes;
                try
                {
                    nodes = SubtreeAnalyzer.LargestFiles(node, 2000);
                }
                catch (NotImplementedException)
                {
                    nodes = node.Children.Where(c => !c.IsDirectory).Take(2000).ToList();
                }
                foreach (var f in nodes) files.Add(f.FullPath);
            }
            return files.Count == 0 ? Array.Empty<LockingProcess>() : platform.FindLockingProcesses(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Array.Empty<LockingProcess>();
        }
    }

    // Runs work on a pool thread. A progress box only appears when the work takes longer than a moment.
    private static async Task<T> WithProgress<T>(Window owner, string text, Func<T> work)
    {
        var task = Task.Run(work);
        if (await Task.WhenAny(task, Task.Delay(250)) == task) return await task;

        var dialog = new ProgressDialog(text);
        var shown = dialog.ShowDialog(owner);
        try
        {
            return await task;
        }
        finally
        {
            dialog.Finish();
            await shown;
        }
    }

    private static void AppendLog(string appDataDir, string mode, long size, Badge badge, string path)
    {
        try
        {
            Directory.CreateDirectory(appDataDir);
            var line = string.Join('\t', DateTime.UtcNow.ToString("O"), mode, size.ToString(), badge.ToString(), path);
            File.AppendAllText(Path.Combine(appDataDir, "deletions.log"), line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never turn a successful delete into an error.
        }
    }
}
