using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DiskLore.Core.Security;

// <AppData>/secrets.json: name -> value. On Windows every value is a base64 DPAPI blob (current user);
// the fallback for systems without a keyring keeps the plain value and relies on 0600 permissions.
internal sealed class FileSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string?> _unprotect;
    private readonly object _gate = new();

    internal FileSecretStore(string path, string backendName, bool isSecure,
        Func<string, string> protect, Func<string, string?> unprotect)
    {
        _path = path;
        BackendName = backendName;
        IsSecure = isSecure;
        _protect = protect;
        _unprotect = unprotect;
    }

    public static FileSecretStore CreateDpapi(string path)
        => new(path, "Windows DPAPI", true, Dpapi.Protect, Dpapi.Unprotect);

    public static FileSecretStore CreatePlain(string path)
        => new(path, "File (0600)", false, v => v, v => v);

    public string BackendName { get; }
    public bool IsSecure { get; }

    public string? Get(string name)
    {
        lock (_gate)
        {
            if (!Read().TryGetValue(name, out var stored)) return null;
            try { return _unprotect(stored); }
            catch (Exception) { return null; }
        }
    }

    public void Set(string name, string value)
    {
        lock (_gate)
        {
            var map = Read();
            map[name] = _protect(value);
            Write(map);
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            var map = Read();
            if (map.Remove(name)) Write(map);
        }
    }

    private Dictionary<string, string> Read()
    {
        try
        {
            if (File.Exists(_path))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
                if (map != null) return new Dictionary<string, string>(map, StringComparer.Ordinal);
            }
        }
        catch (Exception)
        {
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void Write(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(map, Json));
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch (Exception) { }
        }
        File.Move(tmp, _path, true);
    }

    // CryptProtectData / CryptUnprotectData, current-user scope, with the fixed entropy "DiskLore".
    internal static class Dpapi
    {
        private const int CryptProtectUiForbidden = 0x1;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DiskLore");

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        public static string Protect(string plain)
            => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plain), protect: true));

        public static string? Unprotect(string base64)
            => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(base64), protect: false));

        private static byte[] Transform(byte[] input, bool protect)
        {
            var inBlob = ToBlob(input, out var inHandle);
            var entropy = ToBlob(Entropy, out var entropyHandle);
            try
            {
                DataBlob outBlob;
                var ok = protect
                    ? CryptProtectData(ref inBlob, "DiskLore", ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob);
                if (!ok) throw new InvalidOperationException("Windows DPAPI failed (error " + Marshal.GetLastWin32Error() + ").");
                try
                {
                    var result = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, result, 0, result.Length);
                    return result;
                }
                finally
                {
                    LocalFree(outBlob.pbData);
                }
            }
            finally
            {
                inHandle.Free();
                entropyHandle.Free();
            }
        }

        private static DataBlob ToBlob(byte[] data, out GCHandle handle)
        {
            handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            return new DataBlob { cbData = data.Length, pbData = handle.AddrOfPinnedObject() };
        }
    }
}
