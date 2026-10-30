namespace DiskLore.Core.Platform;

public sealed class MacPlatformServices : UnixPlatformServicesBase
{
    public MacPlatformServices()
        : base(Path.Combine(Home, "Library", "Application Support", "DiskLore")) { }

    protected override bool IncludeDrive(string rootPath, string format)
    {
        if (rootPath == "/System/Volumes/Data") return true;
        if (rootPath.StartsWith("/System/Volumes/", StringComparison.Ordinal)) return false;
        if (rootPath.StartsWith("/private/var/vm", StringComparison.Ordinal)) return false;
        if (format is "devfs" or "autofs" or "nullfs") return false;
        return true;
    }

    protected override string FallbackLabel(string rootPath) => rootPath switch
    {
        "/" => "Macintosh HD",
        "/System/Volumes/Data" => "Macintosh HD - Data",
        _ => base.FallbackLabel(rootPath),
    };

    public override DeleteResult MoveToTrash(string path)
    {
        try
        {
            bool isDir = Directory.Exists(path) && new DirectoryInfo(path).LinkTarget == null;
            if (!isDir && !File.Exists(path) && new FileInfo(path).LinkTarget == null)
                return new DeleteResult(true, null);

            var escaped = Path.GetFullPath(path).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var script = $"tell application \"Finder\" to delete POSIX file \"{escaped}\"";
            if (RunTool("osascript", "-e", script) is { ExitCode: 0 })
                return new DeleteResult(true, null);

            // Finder unavailable (no GUI session, automation denied): move into ~/.Trash ourselves.
            var trash = Path.Combine(Home, ".Trash");
            Directory.CreateDirectory(trash);
            var name = UniqueName(Path.GetFileName(path.TrimEnd('/')),
                n => File.Exists(Path.Combine(trash, n)) || Directory.Exists(Path.Combine(trash, n)));
            var dest = Path.Combine(trash, name);
            if (isDir) Directory.Move(path, dest);
            else File.Move(path, dest);
            return new DeleteResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DeleteResult(false, ex.Message);
        }
    }

    public override void RevealInFileManager(string path) => StartDetached("open", "-R", path);

    public override void OpenUri(string uri) => StartDetached("open", uri);
}
