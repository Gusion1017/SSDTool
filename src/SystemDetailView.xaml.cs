using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OxyPlot;

namespace DiskMonitor;

/// <summary>
/// System（PID 4）细分（主窗口右侧栏）：把内核对杂项 I/O 按“文件”和“物理磁盘”两个维度展开。
/// 主窗口每秒调用一次 <see cref="Refresh(DiskIOMonitor)"/>，容量极小，不参与采集线程的计算。
/// </summary>
public partial class SystemDetailView : UserControl
{
    /// <summary>细分列表最多逐条显示的文件数；其余合并成一行，避免极端会话下 UI 卡顿。</summary>
    private const int MaxDisplayFiles = 200;

    private readonly PlotModel _readPieModel;
    private readonly PlotModel _writePieModel;
    private IReadOnlyDictionary<int, string> _diskLabels = new Dictionary<int, string>();

    public SystemDetailView()
    {
        InitializeComponent();

        _readPieModel = PieChart.BuildModel(L.T("读取（按文件）", "Read by File"));
        _writePieModel = PieChart.BuildModel(L.T("写入（按文件）", "Write by File"));
        // 侧栏较窄：扇区外的百分比会超出绘图区被裁掉；占比已由右侧列表的两列给出。
        foreach (var model in new[] { _readPieModel, _writePieModel })
            ((TooltipPieSeries)model.Series[0]).OutsideLabelFormat = "";
        ReadPieView.Model = _readPieModel;
        WritePieView.Model = _writePieModel;
    }

    /// <summary>用采集器的当前快照刷新（记录过程中由主窗口每秒调用）。</summary>
    public void Refresh(DiskIOMonitor monitor) =>
        RefreshSnapshot(monitor.GetSystemBreakdown(), monitor.DiskLabels);

    /// <summary>用给定快照刷新（与采集器解耦，便于复用与测试）。</summary>
    public void RefreshSnapshot(SystemBreakdown breakdown, IReadOnlyDictionary<int, string> diskLabels)
    {
        _diskLabels = new Dictionary<int, string>(diskLabels);
        ApplyLanguage();

        foreach (var model in new[] { _readPieModel, _writePieModel })
        {
            model.Background = ThemePalette.ChartBackground();
            model.TextColor = ThemePalette.ChartText();
        }

        SummaryText.Text = L.T("System（PID 4）", "System (PID 4)")
            + "    " + L.T("读取 ", "Read ") + FormatUtil.Bytes(breakdown.ReadBytes)
            + "    " + L.T("写入 ", "Write ") + FormatUtil.Bytes(breakdown.WriteBytes);

        // 文件行按总 I/O 排序；饼图与行背景共用同一索引的颜色。
        var allFiles = breakdown.Files
            .OrderByDescending(f => f.ReadBytes + f.WriteBytes)
            .ToList();

        var displayFiles = new List<DisplayFile>(Math.Min(allFiles.Count, MaxDisplayFiles) + 1);
        for (int i = 0; i < allFiles.Count && i < MaxDisplayFiles; i++)
            displayFiles.Add(new DisplayFile(allFiles[i].Name, allFiles[i].ReadBytes, allFiles[i].WriteBytes));

        if (allFiles.Count > MaxDisplayFiles)
        {
            long restRead = 0;
            long restWrite = 0;
            for (int i = MaxDisplayFiles; i < allFiles.Count; i++)
            {
                restRead += allFiles[i].ReadBytes;
                restWrite += allFiles[i].WriteBytes;
            }

            int restCount = allFiles.Count - MaxDisplayFiles;
            displayFiles.Add(new DisplayFile(
                L.T($"（其余 {restCount} 个文件）", $"(other {restCount} files)"),
                restRead,
                restWrite));
        }

        FillPie(_readPieModel, displayFiles, read: true);
        FillPie(_writePieModel, displayFiles, read: false);

        var fileRows = new List<IoRow>(displayFiles.Count);
        for (int i = 0; i < displayFiles.Count; i++)
        {
            var f = displayFiles[i];
            fileRows.Add(new IoRow
            {
                Name = DisplayFileName(f.Name),
                ReadText = FormatUtil.Bytes(f.ReadBytes),
                WriteText = FormatUtil.Bytes(f.WriteBytes),
                ReadPercentText = Percent(f.ReadBytes, breakdown.ReadBytes),
                WritePercentText = Percent(f.WriteBytes, breakdown.WriteBytes),
                ColorBrush = PieChart.GetBrush(i),
            });
        }
        FileList.ItemsSource = fileRows;

        var diskRows = breakdown.Disks
            .OrderByDescending(d => d.ReadBytes + d.WriteBytes)
            .Select(d => new DiskRow
            {
                Name = DiskName(d.DiskNumber),
                Volumes = VolumeName(d.DiskNumber),
                ReadText = FormatUtil.Bytes(d.ReadBytes),
                WriteText = FormatUtil.Bytes(d.WriteBytes),
                ReadPercentText = Percent(d.ReadBytes, breakdown.ReadBytes),
                WritePercentText = Percent(d.WriteBytes, breakdown.WriteBytes),
            })
            .ToList();
        DiskList.ItemsSource = diskRows;
    }

    public void ApplyLanguage()
    {
        FilesTab.Header = L.T("按文件", "By File");
        DisksTab.Header = L.T("按磁盘", "By Disk");
        HintText.Text = L.T(
            "每秒刷新。文件名来自 ETW DiskFileIO 映射；页文件、元数据等内核内部 I/O 可能没有文件名，会归入「未解析文件名」。",
            "Refreshes every second. File names come from the ETW DiskFileIO mapping; kernel-internal I/O (pagefile, metadata, ...) may have no name and is grouped as unresolved.");

        _readPieModel.Title = L.T("读取（按文件）", "Read by File");
        _writePieModel.Title = L.T("写入（按文件）", "Write by File");
        _readPieModel.InvalidatePlot(true);
        _writePieModel.InvalidatePlot(true);

        SetHeaders(FileList,
            L.T("文件", "File"), L.T("读取", "Read"), L.T("写入", "Write"),
            L.T("读占比", "Read %"), L.T("写占比", "Write %"));
        SetHeaders(DiskList,
            L.T("磁盘", "Disk"), L.T("卷", "Volumes"), L.T("读取", "Read"), L.T("写入", "Write"),
            L.T("读占比", "Read %"), L.T("写占比", "Write %"));
    }

    public void ApplyTheme()
    {
        foreach (var model in new[] { _readPieModel, _writePieModel })
        {
            model.Background = ThemePalette.ChartBackground();
            model.TextColor = ThemePalette.ChartText();
            model.InvalidatePlot(true);
        }
    }

    private static void FillPie(PlotModel model, IReadOnlyList<DisplayFile> files, bool read)
    {
        var series = (TooltipPieSeries)model.Series[0];
        series.ClearSlices();

        double total = 0;
        foreach (var f in files)
            total += read ? f.ReadBytes : f.WriteBytes;

        for (int i = 0; i < files.Count; i++)
        {
            long value = read ? files[i].ReadBytes : files[i].WriteBytes;
            if (value <= 0)
                continue;

            string fullName = DisplayFileName(files[i].Name);
            double percent = total > 0 ? value / total : 0;
            string label = percent >= PieChart.LabelThresholdPercent ? PieLabel(fullName) : "";
            // 饼图内显示短名，悬停提示保留完整路径；
            // 颜色索引使用统一排序（总 I/O 降序），使两个饼图与列表行颜色一一对应。
            series.AddSlice(label, fullName, value, PieChart.ColorAt(i));
        }

        model.InvalidatePlot(true);
    }

    private static void SetHeaders(DataGrid grid, params string[] headers)
    {
        for (int i = 0; i < headers.Length && i < grid.Columns.Count; i++)
            grid.Columns[i].Header = headers[i];
    }

    private static string DisplayFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return L.T("（未解析文件名）", "(unresolved file name)");
        if (name == DiskIOMonitor.OtherFilesName)
            return L.T("（其他文件）", "(other files)");
        return name;
    }

    /// <summary>饼图内的短标签：只取文件名部分并限长，完整路径留在悬停提示里。</summary>
    private static string PieLabel(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return "";

        string name = fullName.TrimEnd('\\');
        int slash = name.LastIndexOf('\\');
        if (slash >= 0 && slash + 1 < name.Length)
            name = name.Substring(slash + 1);
        if (name.Length > 24)
            name = name.Substring(0, 21) + "...";
        return name;
    }

    private static string Percent(long value, long total) =>
        total > 0 ? $"{value * 100.0 / total:0.#}%" : "0%";

    private static string DiskName(int disk) =>
        disk < 0 ? L.T("未知磁盘", "Unknown disk") : L.T("磁盘 ", "Disk ") + disk;

    private string VolumeName(int disk) =>
        _diskLabels.TryGetValue(disk, out var volumes) ? volumes : "";
}

internal sealed class IoRow
{
    public string Name { get; set; } = "";
    public string ReadText { get; set; } = "";
    public string WriteText { get; set; } = "";
    public string ReadPercentText { get; set; } = "";
    public string WritePercentText { get; set; } = "";
    public Brush? ColorBrush { get; set; }
}

internal sealed class DiskRow
{
    public string Name { get; set; } = "";
    public string Volumes { get; set; } = "";
    public string ReadText { get; set; } = "";
    public string WriteText { get; set; } = "";
    public string ReadPercentText { get; set; } = "";
    public string WritePercentText { get; set; } = "";
}

internal sealed record DisplayFile(string Name, long ReadBytes, long WriteBytes);
