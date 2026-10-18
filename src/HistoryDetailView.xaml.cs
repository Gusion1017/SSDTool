using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OxyPlot;

namespace DiskMonitor;

/// <summary>
/// 历史记录详情（主窗口右侧栏）：某次会话的读写进程分布。
/// 由 MainWindow 在点击历史记录行时填充，不再单独弹窗。
/// </summary>
public partial class HistoryDetailView : UserControl
{
    private readonly PlotModel _readPieModel;
    private readonly PlotModel _writePieModel;

    private HistoryEntry? _entry;
    private Action<HistoryEntry>? _onDelete;

    public HistoryDetailView()
    {
        InitializeComponent();

        _readPieModel = PieChart.BuildModel("");
        _writePieModel = PieChart.BuildModel("");
        // 侧栏较窄：扇区外的百分比会超出绘图区被裁掉，占比改由右侧列表的“占比”列给出。
        foreach (var model in new[] { _readPieModel, _writePieModel })
            ((TooltipPieSeries)model.Series[0]).OutsideLabelFormat = "";
        ReadPieView.Model = _readPieModel;
        WritePieView.Model = _writePieModel;
    }

    /// <summary>展示一条历史记录的详情。</summary>
    /// <param name="onDelete">点击“删除此记录”并确认后的回调（由主窗口持久化并关闭侧栏）。</param>
    public void ShowEntry(HistoryEntry entry, Action<HistoryEntry> onDelete)
    {
        _entry = entry;
        _onDelete = onDelete;
        ApplyLanguage();
        FillCharts();
    }

    public void ApplyLanguage()
    {
        DeleteButton.Content = L.T("删除此记录", "Delete This Record");
        ReadHeader.Text = L.T("读取分布", "Read Distribution");
        WriteHeader.Text = L.T("写入分布", "Write Distribution");
        // 侧栏空间有限：标题由上方区块标题承担，饼图内不再重复。
        _readPieModel.Title = "";
        _writePieModel.Title = "";
        SetHeaders(ReadDetailList, L.T("进程", "Process"), L.T("字节", "Bytes"), L.T("占比", "Share"));
        SetHeaders(WriteDetailList, L.T("进程", "Process"), L.T("字节", "Bytes"), L.T("占比", "Share"));
        UpdateSummary();
        _readPieModel.InvalidatePlot(true);
        _writePieModel.InvalidatePlot(true);
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

    private void UpdateSummary()
    {
        if (_entry == null)
        {
            SummaryText.Text = "";
            return;
        }

        SummaryText.Text =
            L.T("开始", "Start") + " " + _entry.StartTime.ToString("yyyy-MM-dd HH:mm:ss") +
            "    " + L.T("时长", "Duration") + " " + FormatUtil.Duration(_entry.Duration) +
            "    " + L.T("读取", "Read") + " " + FormatUtil.Bytes(_entry.ReadBytes) +
            "    " + L.T("写入", "Write") + " " + FormatUtil.Bytes(_entry.WriteBytes);
    }

    private void FillCharts()
    {
        if (_entry == null)
            return;

        var readItems = _entry.ReadProcesses.Select(p => new ProcessAggregate(p.Name, p.Bytes)).ToList();
        var writeItems = _entry.WriteProcesses.Select(p => new ProcessAggregate(p.Name, p.Bytes)).ToList();
        PieChart.Fill(_readPieModel, readItems);
        PieChart.Fill(_writePieModel, writeItems);

        ReadDetailList.ItemsSource = BuildRows(readItems);
        WriteDetailList.ItemsSource = BuildRows(writeItems);
    }

    private static List<DetailRow> BuildRows(IReadOnlyList<ProcessAggregate> items)
    {
        long total = items.Sum(i => i.Bytes);
        var rows = new List<DetailRow>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            double percent = total > 0 ? items[i].Bytes * 100.0 / total : 0;
            rows.Add(new DetailRow
            {
                Name = items[i].Name,
                BytesText = FormatUtil.Bytes(items[i].Bytes),
                PercentText = $"{percent:0.#}%",
                ColorBrush = PieChart.GetBrush(i),
            });
        }
        return rows;
    }

    private static void SetHeaders(DataGrid grid, string h1, string h2, string h3)
    {
        grid.Columns[0].Header = h1;
        grid.Columns[1].Header = h2;
        grid.Columns[2].Header = h3;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_entry == null)
            return;

        var owner = Window.GetWindow(this);
        string title = L.T("删除", "Delete");
        string question = L.T("确定删除这条历史记录？", "Delete this record?");
        var result = owner != null
            ? MessageBox.Show(owner, question, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _onDelete?.Invoke(_entry);
    }
}

internal sealed class DetailRow
{
    public string Name { get; set; } = "";
    public string BytesText { get; set; } = "";
    public string PercentText { get; set; } = "";
    public Brush? ColorBrush { get; set; }
}
