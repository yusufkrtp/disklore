using System.Text;

namespace DiskLore.Core.Platform;

public sealed class LinuxPlatformServices : UnixPlatformServicesBase
{
    private static readonly HashSet<string> SkippedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "overlay", "squashfs", "autofs", "securityfs", "debugfs",
        "tracefs", "configfs", "fusectl", "pstore", "bpf", "mqueue", "hugetlbfs", "binfmt_misc", "ramfs",
        "rpc_pipefs", "nsfs", "efivarfs", "selinuxfs", "fuse.gvfsd-fuse", "fuse.portal", "fuse.lxcfs", "devfs", "squashfuse",
    };

    private static string DataHome
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(Home, ".local", "share");
        }
    }

    public LinuxPlatformServices() : base(Path.Combine(DataHome, "DiskLore")) { }

    protected override bool IncludeDrive(string rootPath, string format)
    {
        if (SkippedFormats.Contains(format) || format.StartsWith("cgroup", StringComparison.OrdinalIgnoreCase)) return false;
        if (rootPath.StartsWith("/snap/", StringComparison.Ordinal)) return false;
        return true;
    }

    protected override string FallbackLabel(string rootPath) => rootPath == "/" ? "Root" : base.FallbackLabel(rootPath);

    public override DeleteResult MoveToTrash(string path)
    {
        try
        {
            bool isDir = Directory.Exists(path) && new DirectoryInfo(path).LinkTarget == null;
            if (!isDir && !File.Exists(path) && new FileInfo(path).LinkTarget == null)
                return new DeleteResult(true, null);

            var trash = Path.Combine(DataHome, "Trash");
            if (SameDevice(path, DataHome))
                return TrashInto(trash, path, isDir);

            if (RunTool("gio", "trash", path) is { ExitCode: 0 })
                return new DeleteResult(true, null);
            return new DeleteResult(false, "Trash not available on this drive");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DeleteResult(false, ex.Message);
        }
    }

    private static DeleteResult TrashInto(string trash, string path, bool isDir)
    {
        var files = Path.Combine(trash, "files");
        var info = Path.Combine(trash, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);

        var name = UniqueName(Path.GetFileName(path.TrimEnd('/')),
            n => File.Exists(Path.Combine(files, n)) || Directory.Exists(Path.Combine(files, n)) ||
                 File.Exists(Path.Combine(info, n + ".trashinfo")) || new FileInfo(Path.Combine(files, n)).LinkTarget != null);

        var infoText = "[Trash Info]\nPath=" + EncodePath(Path.GetFullPath(path)) + "\nDeletionDate=" +
                       DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss") + "\n";
        var infoPath = Path.Combine(info, name + ".trashinfo");
        File.WriteAllText(infoPath, infoText);

        try
        {
            var dest = Path.Combine(files, name);
            if (isDir) Directory.Move(path, dest);
            else File.Move(path, dest);
        }
        catch
        {
            try { File.Delete(infoPath); } catch (IOException) { }
            throw;
        }
        return new DeleteResult(true, null);
    }

    private static string EncodePath(string path)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            char c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '~'))
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>Approximates "same device" by comparing the longest matching mount point of both paths.</summary>
    private static bool SameDevice(string a, string b)
    {
        try
        {
            return MountOf(a) == MountOf(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string MountOf(string path)
    {
        var full = Path.GetFullPath(path);
        string best = "/";
        foreach (var d in DriveInfo.GetDrives())
        {
            var root = d.RootDirectory.FullName.TrimEnd('/');
            if (root.Length == 0) continue;
            if ((full == root || full.StartsWith(root + "/", StringComparison.Ordinal)) && root.Length > best.Length)
                best = root;
        }
        return best;
    }

    public override void RevealInFileManager(string path)
    {
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? "/";
        StartDetached("xdg-open", dir);
    }

    public override void OpenUri(string uri) => StartDetached("xdg-open", uri);
}
