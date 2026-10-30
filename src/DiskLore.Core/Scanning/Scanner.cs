using System.Collections.Concurrent;
using System.Diagnostics;
using DiskLore.Core.Model;

namespace DiskLore.Core.Scanning;

public sealed class Scanner : IScanner
{
    private const int UnreadableCap = 10_000;

    private static readonly EnumerationOptions ListOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public async Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ct.ThrowIfCancellationRequested();

        var startedUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        var full = Path.GetFullPath(rootPath);
        var pathRoot = Path.GetPathRoot(full) ?? "";
        var isDriveRoot = pathRoot.Length > 0 && string.Equals(full, pathRoot, StringComparison.OrdinalIgnoreCase);
        if (!isDriveRoot) full = Path.TrimEndingDirectorySeparator(full);

        var rootInfo = new DirectoryInfo(full);
        if (!rootInfo.Exists) throw new DirectoryNotFoundException($"Folder not found: {full}");

        var root = new ScanNode(full, null, true);
        try { root.LastWriteUtc = rootInfo.LastWriteTimeUtc; } catch { }

        var state = new WalkState(ct);
        state.Enqueue(root, full);

        Timer? timer = null;
        if (progress != null)
            timer = new Timer(_ => progress.Report(state.Snapshot()), null, 100, 100);

        try
        {
            int workers = Math.Max(1, Environment.ProcessorCount);
            var tasks = new Task[workers];
            for (int i = 0; i < workers; i++)
                tasks[i] = Task.Factory.StartNew(() => Worker(state), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            if (timer != null) await timer.DisposeAsync().ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(state.Snapshot());

        Aggregate(root);

        var unreadable = state.Unreadable.ToArray();
        Array.Sort(unreadable, StringComparer.OrdinalIgnoreCase);
        sw.Stop();
        return new ScanResult(root, startedUtc, sw.Elapsed, unreadable, isDriveRoot);
    }

    private static void Worker(WalkState s)
    {
        var spin = new SpinWait();
        while (!s.Ct.IsCancellationRequested)
        {
            if (!s.Queue.TryDequeue(out var item))
            {
                if (Volatile.Read(ref s.Outstanding) == 0) return;
                spin.SpinOnce(20);
                continue;
            }
            spin = default;
            try
            {
                ProcessDirectory(s, item.Node, item.Path);
            }
            finally
            {
                Interlocked.Decrement(ref s.Outstanding);
            }
        }
    }

    private static void ProcessDirectory(WalkState s, ScanNode dir, string path)
    {
        s.CurrentPath = path;
        Interlocked.Increment(ref s.Folders);

        try
        {
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", ListOptions))
            {
                try
                {
                    AddEntry(s, dir, path, info);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Entry vanished or became unreadable while listing; skip it.
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            dir.AccessDenied = true;
            s.AddUnreadable(path);
        }
    }

    private static void AddEntry(WalkState s, ScanNode dir, string dirPath, FileSystemInfo info)
    {
        var attrs = info.Attributes;
        bool isDir = (attrs & FileAttributes.Directory) != 0;
        var node = new ScanNode(info.Name, dir, isDir);

        string? target = null;
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            try { target = info.LinkTarget; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        try { node.LastWriteUtc = info.LastWriteTimeUtc; } catch { }

        if (target != null)
        {
            // Junction, symlink or mount point: shown, never followed, no size.
            node.IsReparsePoint = true;
            node.LinkTarget = target;
            dir.AddChild(node);
            if (!isDir) Interlocked.Increment(ref s.Files);
            return;
        }

        dir.AddChild(node);
        if (isDir)
        {
            s.Enqueue(node, Path.Join(dirPath, info.Name));
        }
        else
        {
            long len = info is FileInfo fi ? fi.Length : 0;
            node.Size = len;
            Interlocked.Increment(ref s.Files);
            Interlocked.Add(ref s.Bytes, len);
        }
    }

    // Post-order without recursion: a preorder list of directories walked backwards visits
    // every directory after all of its descendants.
    private static void Aggregate(ScanNode root)
    {
        var dirs = new List<ScanNode>();
        var stack = new Stack<ScanNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            dirs.Add(d);
            foreach (var c in d.Children)
                if (c.IsDirectory && !c.IsReparsePoint) stack.Push(c);
        }

        for (int i = dirs.Count - 1; i >= 0; i--)
        {
            var d = dirs[i];
            long size = 0, files = 0, folders = 0;
            var newest = d.LastWriteUtc;
            foreach (var c in d.Children)
            {
                size += c.Size;
                files += c.FileCount;
                if (c.IsDirectory) folders += c.FolderCount + 1;
                if (c.LastWriteUtc > newest) newest = c.LastWriteUtc;
            }
            d.Size = size;
            d.FileCount = files;
            d.FolderCount = folders;
            d.LastWriteUtc = newest;
            d.SortChildren();
        }
    }

    private sealed class WalkState
    {
        public readonly CancellationToken Ct;
        public readonly ConcurrentQueue<(ScanNode Node, string Path)> Queue = new();
        public readonly ConcurrentQueue<string> Unreadable = new();
        public int Outstanding;
        public long Files, Folders, Bytes;
        public string CurrentPath = "";
        private int _unreadableCount;

        public WalkState(CancellationToken ct) => Ct = ct;

        public void Enqueue(ScanNode node, string path)
        {
            Interlocked.Increment(ref Outstanding);
            Queue.Enqueue((node, path));
        }

        public void AddUnreadable(string path)
        {
            // Beyond the cap we keep counting but stop storing paths.
            if (Interlocked.Increment(ref _unreadableCount) <= UnreadableCap) Unreadable.Enqueue(path);
        }

        public ScanProgress Snapshot() => new(
            Interlocked.Read(ref Files), Interlocked.Read(ref Folders), Interlocked.Read(ref Bytes), CurrentPath);
    }
}
