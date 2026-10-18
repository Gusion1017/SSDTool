using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskMonitor;

/// <summary>
/// Toolhelp32 快照 P/Invoke：用于在“开始记录”时，为已存在的进程补全
/// “线程 ID → 进程 ID”和“进程 ID → 映像名”映射。
/// 仅在开始记录时做一次快照，开销极小。
/// </summary>
internal static class NativeMethods
{
    internal const uint TH32CS_SNAPTHREAD = 0x00000004;
    internal const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public int tpBasePri;
        public int tpDeltaPri;
        public uint dwFlags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);
}

/// <summary>
/// 卷/磁盘映射表：设备路径 → 盘符、磁盘号 → 盘符集合。
/// </summary>
internal sealed class VolumeMaps
{
    public Dictionary<string, string> DeviceToDrive { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, string> DiskToDrives { get; } = new();
}

/// <summary>
/// 卷/磁盘映射：QueryDosDevice 得到 “\Device\HarddiskVolumeN → C:”，
/// IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS 得到 “磁盘号 → 盘符”。
/// 只在开始记录、打开细分窗口时调用，开销可忽略。
/// </summary>
internal static class VolumeInfo
{
    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint GENERIC_READ = 0x80000000;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct DISK_EXTENT
    {
        public uint DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr device, uint ioControlCode,
        IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, StringBuilder targetPath, uint max);

    public static VolumeMaps Get()
    {
        var maps = new VolumeMaps();
        var diskDrives = new Dictionary<int, List<string>>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    continue;
                if (!drive.IsReady)
                    continue;

                string letter = drive.Name.TrimEnd('\\'); // "C:"

                var sb = new StringBuilder(260);
                if (QueryDosDeviceW(letter, sb, (uint)sb.Capacity) > 0)
                {
                    string device = sb.ToString();
                    if (device.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
                        maps.DeviceToDrive[device] = letter;
                }

                foreach (int disk in GetDiskNumbers(letter))
                {
                    if (!diskDrives.TryGetValue(disk, out var letters))
                        diskDrives[disk] = letters = new List<string>();
                    letters.Add(letter);
                }
            }
            catch
            {
                // 单个卷查询失败不影响其它卷。
            }
        }

        foreach (var kv in diskDrives)
            maps.DiskToDrives[kv.Key] = string.Join(", ", kv.Value);

        return maps;
    }

    private static IEnumerable<int> GetDiskNumbers(string letter)
    {
        IntPtr handle = CreateFileW(@"\\.\" + letter, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == InvalidHandle)
        {
            // 部分卷需要带读取权限才能打开；失败时静默放弃，磁盘列仍显示磁盘号。
            handle = CreateFileW(@"\\.\" + letter, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }
        if (handle == InvalidHandle)
            yield break;

        try
        {
            const int bufferSize = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                if (!DeviceIoControl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0,
                        buffer, bufferSize, out uint returned, IntPtr.Zero))
                    yield break;

                int count = Marshal.ReadInt32(buffer);
                int extentSize = Marshal.SizeOf<DISK_EXTENT>();
                int max = returned >= 8 ? (int)(returned - 8) / extentSize : 0;
                if (count > max)
                    count = max;

                for (int i = 0; i < count; i++)
                {
                    var extent = Marshal.PtrToStructure<DISK_EXTENT>(
                        IntPtr.Add(buffer, 8 + i * extentSize));
                    yield return (int)extent.DiskNumber;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}

/// <summary>
/// ETW 会话控制（advapi32!ControlTrace）。用于停止程序上次异常退出时遗留的内核会话，
/// 使“开始记录”能自愈，而不会因“会话已被占用”而失败。
/// </summary>
internal static class EtwControl
{
    private const uint EVENT_TRACE_CONTROL_STOP = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNodeHeader
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext; // union: HistoricalContext / {Version, Linkage}
        public ulong TimeStamp;         // union: KernelHandle / TimeStamp
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTraceProperties
    {
        public WNodeHeader Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint ControlTrace(ulong traceHandle, string? sessionName, IntPtr props, uint controlCode);

    /// <summary>
    /// 停止指定名称的 ETW 会话（若存在）。会话不存在或已停止时静默忽略。
    /// </summary>
    internal static void StopSession(string sessionName)
    {
        int structSize = Marshal.SizeOf<EventTraceProperties>();
        int nameBytes = (sessionName.Length + 1) * sizeof(char);
        byte[] buffer = new byte[structSize + nameBytes];

        var props = new EventTraceProperties
        {
            Wnode = new WNodeHeader { BufferSize = (uint)structSize },
            LoggerNameOffset = (uint)structSize,
        };

        IntPtr ptr = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.StructureToPtr(props, ptr, false);
            var bytes = System.Text.Encoding.Unicode.GetBytes(sessionName + '\0');
            Marshal.Copy(bytes, 0, IntPtr.Add(ptr, structSize), bytes.Length);

            // 忽略返回值：会话不存在(ERROR_WMI_INSTANCE_NOT_FOUND=4201)或已停止都视为已清理。
            ControlTrace(0, null, ptr, EVENT_TRACE_CONTROL_STOP);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
