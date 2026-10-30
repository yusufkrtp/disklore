using System.Diagnostics;
using DiskLore.Core.Model;
using DiskLore.Core.Scanning;
using Xunit;
using Xunit.Abstractions;

namespace DiskLore.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disklore-scan-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;

    public ScannerTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            // Remove junctions first so the recursive delete cannot follow them.
            foreach (var d in Directory.EnumerateDirectories(_dir, "*", SearchOption.TopDirectoryOnly))
                if (new DirectoryInfo(d).LinkTarget != null) Directory.Delete(d);
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    private void Write(string rel, int bytes)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[bytes]);
    }

    [Fact]
    public async Task Scan_AggregatesSizesCountsAndSortsBySize()
    {
        Write("a.bin", 1000);
        Write(Path.Combine("sub", "b.bin"), 2000);
        Write(Path.Combine("sub", "deep", "c.txt"), 500);
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));
        var future = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "sub", "deep", "c.txt"), future);

        var result = await new Scanner().ScanAsync(_dir, null, CancellationToken.None);
        var root = result.Root;

        Assert.Equal(3500, root.Size);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(3, root.FolderCount); // sub, deep, empty
        Assert.False(result.IsDriveRoot);
        Assert.Empty(result.UnreadablePaths);

        Assert.Equal(new[] { "sub", "a.bin", "empty" }, root.Children.Select(c => c.Name));
        var sub = root.Children[0];
        Assert.Equal(2500, sub.Size);
        Assert.Equal(2, sub.FileCount);
        Assert.Equal(1, sub.FolderCount);
        Assert.Equal(new[] { "b.bin", "deep" }, sub.Children.Select(c => c.Name));
        Assert.Equal(future, root.LastWriteUtc);
        Assert.Equal(future, sub.LastWriteUtc);
        Assert.Equal(".txt", sub.Children[1].Children[0].Extension);
        Assert.Equal(Path.Combine(_dir, "sub", "deep", "c.txt"), sub.Children[1].Children[0].FullPath);
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        for (int i = 0; i < 20; i++) Write($"f{i}.bin", 10);
        var reports = new List<ScanProgress>();
        var progress = new SyncProgress(reports.Add);

        await new Scanner().ScanAsync(_dir, progress, CancellationToken.None);

        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal(20, last.FilesScanned);
        Assert.Equal(200, last.BytesScanned);
    }

    [Fact]
    public async Task Scan_DoesNotDoubleCountJunctions()
    {
        if (!OperatingSystem.IsWindows()) return;

        for (int i = 0; i < 5; i++) Write(Path.Combine("real", $"big{i}.bin"), 10_000);
        var link = Path.Combine(_dir, "link");
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{Path.Combine(_dir, "real")}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        using (var p = Process.Start(psi)!) { p.WaitForExit(); Assert.Equal(0, p.ExitCode); }

        var result = await new Scanner().ScanAsync(_dir, null, CancellationToken.None);

        Assert.Equal(50_000, result.Root.Size);
        Assert.Equal(5, result.Root.FileCount);
        var node = result.Root.Children.Single(c => c.Name == "link");
        Assert.True(node.IsReparsePoint);
        Assert.NotNull(node.LinkTarget);
        Assert.Equal(0, node.Size);
        Assert.Empty(node.Children);
    }

    [Fact]
    public async Task Scan_CancelledTokenThrows()
    {
        Write("a.bin", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new Scanner().ScanAsync(_dir, null, cts.Token));
    }

    [Fact]
    public async Task Scan_CancelDuringWalkThrows()
    {
        for (int d = 0; d < 30; d++)
            for (int f = 0; f < 30; f++) Write(Path.Combine($"d{d}", $"f{f}.bin"), 1);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(_ => cts.Cancel());
        // Cancel as soon as the first progress report arrives (or the scan finishes first and the test is moot).
        var task = new Scanner().ScanAsync(_dir, progress, cts.Token);
        try { await task; }
        catch (OperationCanceledException) { return; }
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Scan_MissingFolderThrows()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new Scanner().ScanAsync(Path.Combine(_dir, "nope"), null, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Manual")]
    public async Task Manual_RealScan()
    {
        if (Environment.GetEnvironmentVariable("DISKLORE_MANUAL") != "1") return;
        var paths = (Environment.GetEnvironmentVariable("DISKLORE_SCAN_PATHS") ?? @"C:\Program Files")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var sw = Stopwatch.StartNew();
            var r = await new Scanner().ScanAsync(path, null, CancellationToken.None);
            sw.Stop();
            _out.WriteLine($"SCAN {path}: {sw.Elapsed.TotalSeconds:0.00}s size={SizeFormat.Format(r.Root.Size)} files={r.Root.FileCount:N0} folders={r.Root.FolderCount:N0} unreadable={r.UnreadablePaths.Count}");
            foreach (var c in r.Root.Children.Take(5))
                _out.WriteLine($"   {SizeFormat.Format(c.Size),10}  {c.Name}{(c.IsReparsePoint ? " -> " + c.LinkTarget : "")}");
            Console.Error.WriteLine($"SCAN {path}: {sw.Elapsed.TotalSeconds:0.00}s size={SizeFormat.Format(r.Root.Size)} files={r.Root.FileCount:N0} folders={r.Root.FolderCount:N0} unreadable={r.UnreadablePaths.Count}");
            foreach (var c in r.Root.Children.Take(5))
                Console.Error.WriteLine($"   {SizeFormat.Format(c.Size),10}  {c.Name}{(c.IsReparsePoint ? " -> " + c.LinkTarget : "")}");
        }
    }

    // Progress<T> posts to the thread pool; tests want the callback inline.
    private sealed class SyncProgress(Action<ScanProgress> callback) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) { lock (this) callback(value); }
    }
}
