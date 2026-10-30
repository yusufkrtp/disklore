using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiskLore.Core.Platform;

[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsInterop
{
    // ---- Shell file operation (recycle bin) ----

    public const uint FO_DELETE = 0x0003;
    public const ushort FOF_SILENT = 0x0004;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_WANTNUKEWARNING = 0x4000;

    // The native struct is packed to 1 byte on 32-bit and naturally aligned on 64-bit.
    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public nint pFrom;
        public nint pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public nint lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    public static partial int SHFileOperation(ref SHFILEOPSTRUCTW op);

    // ---- Restart Manager ----

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public uint ftLow;
        public uint ftHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char strAppName[256];
        public fixed char strServiceShortName[64];
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int bRestartable;
    }

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmStartSession")]
    public static partial int RmStartSession(out uint sessionHandle, int flags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmRegisterResources")]
    public static partial int RmRegisterResources(uint sessionHandle, uint nFiles, nint* fileNames,
        uint nApplications, nint applications, uint nServices, nint services);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmGetList")]
    public static partial int RmGetList(uint sessionHandle, out uint procInfoNeeded, ref uint procInfo,
        RM_PROCESS_INFO* affectedApps, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmEndSession")]
    public static partial int RmEndSession(uint sessionHandle);
}
