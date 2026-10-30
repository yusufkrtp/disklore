using System.IO.Compression;
using System.Text;
using DiskLore.Core.Model;

namespace DiskLore.Core.Snapshots;

// .dlsnap = gzip(BinaryWriter, UTF-8):
//   "DLSN" (4 bytes), int32 version (1), string root path, int64 taken ticks (UTC),
//   int64 total size, int32 entry count, then per entry: string relative path, int64 value.
// value = (size << 1) | isFolder, so the folder flag costs no extra field and sizes stay far below 2^62.
public sealed class SnapshotStore
{
    private const int Version = 1;
    private const int Keep = 10;
    private const long MinFileSize = 50L * 1024 * 1024;
    private static readonly byte[] Magic = "DLSN"u8.ToArray();
    private static readonly StringComparison PathCompare =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly string _directory;

    public SnapshotStore(string directory)
    {
        _directory = directory;
    }

    // Keeps the newest 10 per root.
    public SnapshotInfo Save(ScanResult result)
    {
        Directory.CreateDirectory(_directory);

        var root = result.Root;
        var taken = result.StartedUtc.Kind == DateTimeKind.Local ? result.StartedUtc.ToUniversalTime() : result.StartedUtc;
        var entries = Collect(root);

        var baseName = Sanitize(root.Name) + "-" + taken.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(_directory, baseName + ".dlsnap");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(_directory, $"{baseName}-{n}.dlsnap");

        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(gz, Encoding.UTF8))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(root.Name);
            w.Write(taken.Ticks);
            w.Write(root.Size);
            w.Write(entries.Count);
            foreach (var (rel, value) in entries)
            {
                w.Write(rel);
                w.Write(value);
            }
        }
        File.Move(tmp, path, true);

        var info = new SnapshotInfo(path, root.Name, new DateTime(taken.Ticks, DateTimeKind.Utc), root.Size);
        Prune(root.Name);
        return info;
    }

    // Newest first. Reads only the header of each file; unreadable files are skipped.
    public IReadOnlyList<SnapshotInfo> List(string rootPath)
    {
        var found = new List<SnapshotInfo>();
        if (!Directory.Exists(_directory)) return found;

        var want = Normalize(rootPath);
        foreach (var file in Directory.EnumerateFiles(_directory, "*.dlsnap"))
        {
            var info = ReadHeader(file);
            if (info != null && string.Equals(Normalize(info.RootPath), want, PathCompare)) found.Add(info);
        }
        found.Sort(static (a, b) =>
        {
            int c = b.TakenUtc.CompareTo(a.TakenUtc);
            return c != 0 ? c : string.CompareOrdinal(b.FilePath, a.FilePath);
        });
        return found;
    }

    public Snapshot Load(SnapshotInfo info)
    {
        using var fs = new FileStream(info.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var r = new BinaryReader(gz, Encoding.UTF8);
        var header = ReadHeader(r);

        int count = r.ReadInt32();
        var sizes = new Dictionary<string, long>(count, StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var rel = r.ReadString();
            var value = r.ReadInt64();
            sizes[rel] = value >> 1;
            if ((value & 1) != 0) folders.Add(rel);
        }
        return new Snapshot(header.Root, new DateTime(header.Ticks, DateTimeKind.Utc), sizes, folders);
    }

    private static List<(string Rel, long Value)> Collect(ScanNode root)
    {
        var list = new List<(string, long)>();
        var stack = new Stack<(ScanNode Node, string Rel)>();
        stack.Push((root, ""));
        while (stack.Count > 0)
        {
            var (node, rel) = stack.Pop();
            if (node.IsDirectory)
            {
                list.Add((rel, (node.Size << 1) | 1));
                foreach (var c in node.Children)
                    stack.Push((c, rel.Length == 0 ? c.Name : rel + Path.DirectorySeparatorChar + c.Name));
            }
            else if (node.Size >= MinFileSize)
            {
                list.Add((rel, node.Size << 1));
            }
        }
        return list;
    }

    private void Prune(string rootPath)
    {
        var all = List(rootPath);
        for (int i = Keep; i < all.Count; i++)
        {
            try { File.Delete(all[i].FilePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static SnapshotInfo? ReadHeader(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new BinaryReader(gz, Encoding.UTF8);
            var h = ReadHeader(r);
            return new SnapshotInfo(file, h.Root, new DateTime(h.Ticks, DateTimeKind.Utc), h.Total);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    private static (string Root, long Ticks, long Total) ReadHeader(BinaryReader r)
    {
        var magic = r.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a DiskLore snapshot.");
        int version = r.ReadInt32();
        if (version != Version) throw new InvalidDataException($"Unsupported snapshot version {version}.");
        return (r.ReadString(), r.ReadInt64(), r.ReadInt64());
    }

    private static string Normalize(string path) =>
        path.Length > 0 && Path.GetPathRoot(path) is { } pr && pr.Length == path.Length ? path : path.TrimEnd('\\', '/');

    private static string Sanitize(string rootPath)
    {
        var invalid = Path.GetInvalidFileNameChars();
        rootPath = rootPath.TrimEnd('\\', '/');
        var sb = new StringBuilder(rootPath.Length);
        foreach (var ch in rootPath)
            sb.Append(ch is '\\' or '/' or ':' || Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString();
        return s.Length == 0 ? "_" : s;
    }
}
