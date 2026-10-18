namespace DiskMonitor;

internal enum ByteUnit { Auto, B, KB, MB, GB }

/// <summary>字节数 / 时长格式化工具。字节单位可全局切换。</summary>
internal static class FormatUtil
{
    /// <summary>当前字节显示单位（Auto 表示按大小自动选择）。</summary>
    public static ByteUnit Unit = ByteUnit.Auto;

    private static readonly string[] UnitNames = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(double bytes)
    {
        switch (Unit)
        {
            case ByteUnit.B:
                return $"{bytes:0} B";
            case ByteUnit.KB:
                return $"{bytes / 1024.0:0.##} KB";
            case ByteUnit.MB:
                return $"{bytes / (1024.0 * 1024.0):0.##} MB";
            case ByteUnit.GB:
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";
            default:
                double v = bytes;
                int u = 0;
                while (v >= 1024 && u < UnitNames.Length - 1)
                {
                    v /= 1024;
                    u++;
                }
                return $"{v:0.##} {UnitNames[u]}";
        }
    }

    public static string Bytes(long bytes) => Bytes((double)bytes);

    public static string Duration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
