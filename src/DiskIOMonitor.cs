using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace DiskMonitor;

/// <summary>
/// 基于 ETW 内核事件（Microsoft-Windows-Kernel-Disk）的低开销逐进程磁盘读写监控器。
/// 采集在内核侧完成：不注入任何进程、不挂 Hook、不轮询 WMI/性能计数器。
/// </summary>
public sealed class DiskIOMonitor : IDisposable
{
    /// <summary>System 进程（内核线程宿主）的固定 PID。</summary>
    private const int SystemProcessId = 4;

    /// <summary>System 细分最多保留的文件名数量，超出后归入“其他文件”，避免长会话内存无界增长。</summary>
    private const int MaxSystemFiles = 4096;

    /// <summary>“其他文件”桶的哨兵键（正常文件名不会以 \u0001 开头）。</summary>
    public const string OtherFilesName = "\u0001other";

    private readonly ConcurrentDictionary<int, ProcessStats> _processes = new();
    private readonly ConcurrentDictionary<int, int> _tidToPid = new();
    private readonly ConcurrentDictionary<int, string> _pidToName = new();

    // System 的 I/O 细分：按文件（文件名已尽量转成用户路径）、按物理磁盘。
    private readonly ConcurrentDictionary<string, MutableIo> _systemFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, MutableIo> _systemDisks = new();

    private IReadOnlyDictionary<string, string> _deviceToDrive = new Dictionary<string, string>();
    private IReadOnlyDictionary<int, string> _diskToDrives = new Dictionary<int, string>();
    private string _windowsDirectory = "";

    private long _totalRead;
    private long _totalWrite;

    private TraceEventSession? _session;
    private Task? _processTask;
    private volatile bool _disposed;

    /// <summary>记录过程中的异步错误（如内核会话被占用、权限不足）。</summary>
    public event Action<string>? Error;

    public bool IsRecording => _session != null;

    public long TotalReadBytes => Interlocked.Read(ref _totalRead);
    public long TotalWriteBytes => Interlocked.Read(ref _totalWrite);

    /// <summary>磁盘号 → 盘符（如 0 → "C:, D:"），开始记录时建立；用于 System 细分窗口显示。</summary>
    public IReadOnlyDictionary<int, string> DiskLabels => _diskToDrives;

    /// <summary>是否已采集到任何 System 细分数据（用于决定细分入口是否可用）。</summary>
    public bool HasSystemIo => !_systemFiles.IsEmpty;

    /// <summary>开始记录。可能同步抛出异常（权限不足、会话被占用等）。</summary>
    public void Start()
    {
        ThrowIfDisposed();
        if (_session != null)
            return;

        // 清理上次异常退出（被强杀/崩溃）可能遗留的内核会话，避免“会话已被占用”。
        EtwControl.StopSession(KernelTraceEventParser.KernelSessionName);

        // 卷/磁盘映射：把内核侧 \Device\HarddiskVolumeN 路径转成盘符路径，并把磁盘号映射到盘符。
        var volumeMaps = VolumeInfo.Get();
        _deviceToDrive = volumeMaps.DeviceToDrive;
        _diskToDrives = volumeMaps.DiskToDrives;
        _windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // 为“开始记录前就已存在”的进程补全 线程ID→进程ID、进程ID→名称 映射。
        SeedThreadProcessMaps();

        var session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
        try
        {
            // DiskIO 事件只携带 IssuingThreadId，需配合 Process/Thread 关键字做线程→进程归属；
            // DiskFileIO 提供 FileKey→文件名的映射，供 System 细分按文件聚合（额外开销很小）。
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.DiskIO |
                KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.Process |
                KernelTraceEventParser.Keywords.Thread);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        session.Source.Kernel.DiskIORead += OnDiskIORead;
        session.Source.Kernel.DiskIOWrite += OnDiskIOWrite;
        session.Source.Kernel.ThreadStart += OnThreadStart;
        session.Source.Kernel.ProcessStart += OnProcessStart;

        _session = session;
        _processTask = Task.Run(() => PumpEvents(session));
    }

    /// <summary>停止记录，冻结统计并释放内核会话。同步执行，确保会话真正停止、不残留。</summary>
    public void Stop()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session == null)
            return;

        var task = _processTask;
        _processTask = null;

        // Dispose 会停止内核会话，使阻塞中的 Source.Process() 返回。
        // 同步等待可确保进程退出前会话已被真正停止，避免“停止后仍残留”。
        try { session.Dispose(); } catch { }
        try { task?.Wait(TimeSpan.FromSeconds(5)); } catch { }
    }

    /// <summary>清空累计统计（不影响正在进行的记录）。</summary>
    public void Reset()
    {
        _processes.Clear();
        _systemFiles.Clear();
        _systemDisks.Clear();
        Interlocked.Exchange(ref _totalRead, 0);
        Interlocked.Exchange(ref _totalWrite, 0);
    }

    /// <summary>返回按进程名聚合后的全部进程读写字节数（按字节数降序，供饼图/列表/历史使用）。</summary>
    internal IReadOnlyList<ProcessAggregate> GetProcesses(bool read)
    {
        var byName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _processes)
        {
            var bytes = read
                ? Interlocked.Read(ref kv.Value.ReadBytes)
                : Interlocked.Read(ref kv.Value.WriteBytes);
            if (bytes <= 0)
                continue;

            var name = string.IsNullOrEmpty(kv.Value.Name) ? "未知进程" : kv.Value.Name;
            byName.TryGetValue(name, out var cur);
            byName[name] = cur + bytes;
        }

        return byName
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ProcessAggregate(kv.Key, kv.Value))
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    private void PumpEvents(TraceEventSession session)
    {
        try
        {
            // 阻塞式消费事件流，直到 Stop() 被调用。
            session.Source.Process();
        }
        catch (Exception ex)
        {
            if (_disposed)
                return;

            // 正常停止时 Stop() 已把 _session 清空，Process() 可能以异常方式返回，此时不应报错。
            if (!ReferenceEquals(_session, session))
                return;

            // 仅当当前会话仍是本会话时才清空引用，避免误清新会话。
            Interlocked.CompareExchange(ref _session, null, session);
            try { session.Dispose(); } catch { }
            Error?.Invoke(ex.Message);
        }
    }

    private void OnDiskIORead(DiskIOTraceData data) => AddIo(data, isRead: true);

    private void OnDiskIOWrite(DiskIOTraceData data) => AddIo(data, isRead: false);

    private void AddIo(DiskIOTraceData data, bool isRead)
    {
        int transferSize = data.TransferSize;
        if (transferSize <= 0)
            return;

        int threadId = data.ThreadID;
        int key;
        string name;
        if (threadId > 0 && _tidToPid.TryGetValue(threadId, out var pid) && pid >= 0)
        {
            key = pid;
            name = _pidToName.TryGetValue(pid, out var n) ? n : $"进程 {pid}";
        }
        else
        {
            // 未知线程使用独立负值键，避免互相覆盖；饼图聚合时统一归为“未知进程”。
            key = -threadId - 1;
            name = "未知进程";
        }

        var stats = _processes.GetOrAdd(key, _ => new ProcessStats { Name = name });
        if (string.IsNullOrEmpty(stats.Name))
            stats.Name = name;

        if (isRead)
        {
            Interlocked.Add(ref stats.ReadBytes, transferSize);
            Interlocked.Add(ref _totalRead, transferSize);
        }
        else
        {
            Interlocked.Add(ref stats.WriteBytes, transferSize);
            Interlocked.Add(ref _totalWrite, transferSize);
        }

        // System（PID 4）额外做文件/磁盘细分；其它进程只保留进程级统计。
        if (key == SystemProcessId)
            AddSystemIo(data, transferSize, isRead);
    }

    /// <summary>把 System 的每一次 I/O 同时记到“文件”和“物理磁盘”两个子桶。</summary>
    private void AddSystemIo(DiskIOTraceData data, int transferSize, bool isRead)
    {
        string file = "";
        try
        {
            file = NormalizeFileName(data.FileName);
        }
        catch
        {
            // 个别事件的字符串字段可能无法解析，归入“未解析文件名”。
        }

        if (file.Length > 0 && !_systemFiles.ContainsKey(file) && _systemFiles.Count >= MaxSystemFiles)
            file = OtherFilesName;

        var fileStats = _systemFiles.GetOrAdd(file, _ => new MutableIo());
        var diskStats = _systemDisks.GetOrAdd(data.DiskNumber, _ => new MutableIo());

        if (isRead)
        {
            Interlocked.Add(ref fileStats.ReadBytes, transferSize);
            Interlocked.Add(ref diskStats.ReadBytes, transferSize);
        }
        else
        {
            Interlocked.Add(ref fileStats.WriteBytes, transferSize);
            Interlocked.Add(ref diskStats.WriteBytes, transferSize);
        }
    }

    /// <summary>
    /// 把内核侧文件名尽量转成用户习惯的路径：\??\C:\...、\SystemRoot\...、\Device\HarddiskVolumeN\...
    /// 转不动的保持原样（仍比“未解析”有信息量）。
    /// </summary>
    private string NormalizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        const string ntPrefix = @"\??\";
        const string systemRoot = @"\SystemRoot";
        const string systemRootSlash = @"\SystemRoot\";

        if (name.StartsWith(ntPrefix, StringComparison.Ordinal))
            name = name.Substring(ntPrefix.Length);
        else if (name.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
            return _windowsDirectory;
        else if (name.StartsWith(systemRootSlash, StringComparison.OrdinalIgnoreCase))
            name = Path.Combine(_windowsDirectory, name.Substring(systemRootSlash.Length));

        if (name.Length == 0)
            return "";

        if (_deviceToDrive.TryGetValue(name, out var drive))
            return drive;

        foreach (var kv in _deviceToDrive)
        {
            if (name.StartsWith(kv.Key + "\\", StringComparison.OrdinalIgnoreCase))
                return kv.Value + name.Substring(kv.Key.Length);
        }

        return name;
    }

    /// <summary>返回 System（PID 4）的按文件/按磁盘细分快照（供细分窗口使用）。</summary>
    public SystemBreakdown GetSystemBreakdown()
    {
        var files = new List<SystemFileBreakdown>();
        long totalRead = 0;
        long totalWrite = 0;
        foreach (var kv in _systemFiles)
        {
            long read = Interlocked.Read(ref kv.Value.ReadBytes);
            long write = Interlocked.Read(ref kv.Value.WriteBytes);
            if (read == 0 && write == 0)
                continue;

            files.Add(new SystemFileBreakdown { Name = kv.Key, ReadBytes = read, WriteBytes = write });
            totalRead += read;
            totalWrite += write;
        }

        var disks = new List<SystemDiskBreakdown>();
        foreach (var kv in _systemDisks)
        {
            long read = Interlocked.Read(ref kv.Value.ReadBytes);
            long write = Interlocked.Read(ref kv.Value.WriteBytes);
            if (read == 0 && write == 0)
                continue;

            disks.Add(new SystemDiskBreakdown { DiskNumber = kv.Key, ReadBytes = read, WriteBytes = write });
        }

        return new SystemBreakdown
        {
            ReadBytes = totalRead,
            WriteBytes = totalWrite,
            Files = files,
            Disks = disks,
        };
    }

    private void OnThreadStart(ThreadTraceData data) => _tidToPid[data.ThreadID] = data.ProcessID;

    private void OnProcessStart(ProcessTraceData data)
    {
        var name = data.ImageFileName;
        if (!string.IsNullOrEmpty(name))
            _pidToName[data.ProcessID] = Path.GetFileName(name);
    }

    private void SeedThreadProcessMaps()
    {
        _tidToPid.Clear();
        _pidToName.Clear();

        // 1) 进程快照：PID → 映像名
        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap != IntPtr.Zero && snap != new IntPtr(-1))
        {
            var pe = new NativeMethods.PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>(),
            };
            if (NativeMethods.Process32FirstW(snap, ref pe))
            {
                do
                {
                    var name = Path.GetFileName(pe.szExeFile);
                    if (!string.IsNullOrEmpty(name))
                        _pidToName[(int)pe.th32ProcessID] = name;
                }
                while (NativeMethods.Process32NextW(snap, ref pe));
            }
            NativeMethods.CloseHandle(snap);
        }

        // 2) 线程快照：线程 ID → 所属进程 ID
        snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPTHREAD, 0);
        if (snap != IntPtr.Zero && snap != new IntPtr(-1))
        {
            var te = new NativeMethods.THREADENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.THREADENTRY32>(),
            };
            if (NativeMethods.Thread32First(snap, ref te))
            {
                do
                {
                    _tidToPid[(int)te.th32ThreadID] = (int)te.th32OwnerProcessID;
                }
                while (NativeMethods.Thread32Next(snap, ref te));
            }
            NativeMethods.CloseHandle(snap);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DiskIOMonitor));
    }
}

internal sealed class ProcessStats
{
    public volatile string Name = "";
    public long ReadBytes;
    public long WriteBytes;
}

/// <summary>可变累加桶（System 文件/磁盘细分用）。</summary>
internal sealed class MutableIo
{
    public long ReadBytes;
    public long WriteBytes;
}

internal readonly record struct ProcessAggregate(string Name, long Bytes);

/// <summary>System（PID 4）细分快照。Name 为空表示文件名未解析；等于 OtherFilesName 表示“其他文件”。</summary>
public sealed class SystemBreakdown
{
    public long ReadBytes { get; init; }
    public long WriteBytes { get; init; }
    public IReadOnlyList<SystemFileBreakdown> Files { get; init; } = Array.Empty<SystemFileBreakdown>();
    public IReadOnlyList<SystemDiskBreakdown> Disks { get; init; } = Array.Empty<SystemDiskBreakdown>();
}

public sealed class SystemFileBreakdown
{
    public string Name { get; init; } = "";
    public long ReadBytes { get; init; }
    public long WriteBytes { get; init; }
}

public sealed class SystemDiskBreakdown
{
    public int DiskNumber { get; init; }
    public long ReadBytes { get; init; }
    public long WriteBytes { get; init; }
}
