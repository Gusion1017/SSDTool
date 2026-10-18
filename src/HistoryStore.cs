using System.IO;
using System.Text.Json;

namespace DiskMonitor;

/// <summary>某次会话中单个进程的读写字节数（历史详情用）。</summary>
public sealed class HistoryProcess
{
    public string Name { get; set; } = "";
    public long Bytes { get; set; }
}

/// <summary>一次“开始记录 → 停止记录”的会话统计（含进程级明细）。</summary>
public sealed class HistoryEntry
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long ReadBytes { get; set; }
    public long WriteBytes { get; set; }
    public List<HistoryProcess> ReadProcesses { get; set; } = new();
    public List<HistoryProcess> WriteProcesses { get; set; } = new();

    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>历史记录的本地持久化（JSON 文件，跨进程运行保留）。</summary>
internal static class HistoryStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskMonitor", "history.json");

    public static List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
            }
        }
        catch
        {
            // 历史文件损坏或不可读时，忽略并返回空列表。
        }
        return new List<HistoryEntry>();
    }

    public static void Save(IReadOnlyList<HistoryEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写历史失败不影响主功能。
        }
    }
}
