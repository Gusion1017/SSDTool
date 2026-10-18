using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace DiskMonitor;

public partial class MainWindow : Window
{
    private readonly DiskIOMonitor _monitor = new();

    private readonly DispatcherTimer _timer;
    private readonly PlotModel _waveModel;
    private readonly PlotModel _lineModel;
    private readonly PlotModel _pieReadModel;
    private readonly PlotModel _pieWriteModel;

    private readonly LineSeries _waveReadSeries = new();
    private readonly LineSeries _waveWriteSeries = new();
    private readonly LineSeries _readSeries = new();
    private readonly LineSeries _writeSeries = new();

    /// <summary>波形图时间轴：固定显示最近一分钟（滚动窗口）。</summary>
    private readonly DateTimeAxis _waveAxis = new()
    {
        Position = AxisPosition.Bottom,
        StringFormat = "HH:mm:ss",
    };

    private long _lastRead;
    private long _lastWrite;
    private double _lastRateRead;
    private double _lastRateWrite;
    private DateTime _lastSampleTime;
    private bool _firstSample = true;

    private DateTime _recordingStartedAt;
    private long _sessionStartRead;
    private long _sessionStartWrite;

    private readonly List<HistoryEntry> _history;
    private bool _initialized;

    /// <summary>右侧栏当前展示的内容；Hidden 表示侧栏收起。</summary>
    private SidebarMode _sidebarMode = SidebarMode.Hidden;

    /// <summary>右侧栏里正在展示的历史记录（删除后自动收起侧栏）。</summary>
    private HistoryEntry? _sidebarHistoryEntry;

    public MainWindow()
    {
        InitializeComponent();

        AppSettings.Load();
        _history = HistoryStore.Load();

        _monitor.Error += OnMonitorError;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnSampleTick;

        _waveModel = BuildRateModel();
        _lineModel = BuildCumulativeModel();
        _pieReadModel = PieChart.BuildModel(L.T("读取分布", "Read Distribution"));
        _pieWriteModel = PieChart.BuildModel(L.T("写入分布", "Write Distribution"));

        WavePlotView.Model = _waveModel;
        LinePlotView.Model = _lineModel;
        PieReadPlotView.Model = _pieReadModel;
        PieWritePlotView.Model = _pieWriteModel;

        UnitCombo.SelectedIndex = 0;
        LangCombo.SelectedIndex = AppSettings.Language == AppLanguage.English ? 1 : 0;
        ThemeCombo.SelectedIndex = AppSettings.Theme == AppTheme.Dark ? 1 : 0;

        ApplyTheme();
        ApplyLanguage();
        RefreshHistoryList();
        UpdateUi();
        _initialized = true;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor.IsRecording)
            return;

        try
        {
            _monitor.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("无法开始记录：\n\n", "Cannot start recording:\n\n") + ex.Message,
                L.T("启动失败", "Start Failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 每次“开始记录”都从零开始：自动清空上一次的累计统计（历史记录不受影响）。
        _recordingStartedAt = DateTime.Now;
        ResetStatistics();

        _sessionStartRead = _monitor.TotalReadBytes;
        _sessionStartWrite = _monitor.TotalWriteBytes;
        _firstSample = true;
        _lastSampleTime = DateTime.Now;
        _timer.Start();
        UpdateUi();
        if (_sidebarMode == SidebarMode.System)
            SystemDetailSidebar.Refresh(_monitor);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRecording();

    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetStatistics();

    /// <summary>清空累计统计（不影响历史记录）。重置按钮与“开始下一次记录”都会调用。</summary>
    private void ResetStatistics()
    {
        _monitor.Reset();
        _readSeries.Points.Clear();
        _writeSeries.Points.Clear();
        _waveReadSeries.Points.Clear();
        _waveWriteSeries.Points.Clear();
        _firstSample = true;
        _lastRead = 0;
        _lastWrite = 0;
        _lastRateRead = 0;
        _lastRateWrite = 0;
        RateReadText.Text = FormatUtil.Bytes(0) + "/s";
        RateWriteText.Text = FormatUtil.Bytes(0) + "/s";
        RebuildPies();
        UpdateSummary();
        UpdateUi();
        _waveModel.InvalidatePlot(true);
        _lineModel.InvalidatePlot(true);
        if (_sidebarMode == SidebarMode.System)
            SystemDetailSidebar.Refresh(_monitor);
    }

    private void StopRecording()
    {
        if (!_monitor.IsRecording)
            return;

        _monitor.Stop();
        _timer.Stop();

        var entry = BuildHistoryEntry();
        _history.Add(entry);
        HistoryStore.Save(_history);
        RefreshHistoryList();
        UpdateUi();
        // 停止后补一次快照，让侧栏展示最终数字（而不是上一秒的）。
        if (_sidebarMode == SidebarMode.System)
            SystemDetailSidebar.Refresh(_monitor);
    }

    private HistoryEntry BuildHistoryEntry()
    {
        return new HistoryEntry
        {
            StartTime = _recordingStartedAt,
            EndTime = DateTime.Now,
            ReadBytes = _monitor.TotalReadBytes - _sessionStartRead,
            WriteBytes = _monitor.TotalWriteBytes - _sessionStartWrite,
            ReadProcesses = _monitor.GetProcesses(read: true)
                .Select(a => new HistoryProcess { Name = a.Name, Bytes = a.Bytes }).ToList(),
            WriteProcesses = _monitor.GetProcesses(read: false)
                .Select(a => new HistoryProcess { Name = a.Name, Bytes = a.Bytes }).ToList(),
        };
    }

    private void OnSampleTick(object? sender, EventArgs e)
    {
        long read = _monitor.TotalReadBytes;
        long write = _monitor.TotalWriteBytes;
        var now = DateTime.Now;

        double dt = (now - _lastSampleTime).TotalSeconds;
        double rateRead = 0;
        double rateWrite = 0;
        if (!_firstSample && dt > 0)
        {
            rateRead = (read - _lastRead) / dt;
            rateWrite = (write - _lastWrite) / dt;
        }

        _waveReadSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), rateRead));
        _waveWriteSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), rateWrite));
        _readSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), read));
        _writeSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), write));

        UpdateWaveWindow(now);
        TrimWaveSeries(now);

        _lastRateRead = rateRead;
        _lastRateWrite = rateWrite;
        RateReadText.Text = FormatUtil.Bytes(rateRead) + "/s";
        RateWriteText.Text = FormatUtil.Bytes(rateWrite) + "/s";

        _lastRead = read;
        _lastWrite = write;
        _lastSampleTime = now;
        _firstSample = false;

        RebuildPies();
        UpdateSummary();
        UpdateRecordTime();
        if (_sidebarMode == SidebarMode.System)
            SystemDetailSidebar.Refresh(_monitor);
        _waveModel.InvalidatePlot(true);
        _lineModel.InvalidatePlot(true);
    }

    private void RebuildPies()
    {
        RebuildPie(_pieReadModel, ReadList, _monitor.GetProcesses(read: true));
        RebuildPie(_pieWriteModel, WriteList, _monitor.GetProcesses(read: false));
    }

    /// <summary>波形图横轴固定为一分钟：开始阶段为 [开始, 开始+60s]，之后滚动为 [now-58s, now+2s]。</summary>
    private void UpdateWaveWindow(DateTime now)
    {
        double start = DateTimeAxis.ToDouble(_recordingStartedAt);
        double lo = Math.Max(start, DateTimeAxis.ToDouble(now.AddSeconds(-58)));
        double hi = Math.Max(DateTimeAxis.ToDouble(_recordingStartedAt.AddSeconds(60)), DateTimeAxis.ToDouble(now.AddSeconds(2)));
        _waveAxis.Minimum = lo;
        _waveAxis.Maximum = hi;
    }

    /// <summary>移除窗口外已不可见的旧点，避免长时间记录时波形数据无限增长。</summary>
    private void TrimWaveSeries(DateTime now)
    {
        double cut = DateTimeAxis.ToDouble(now.AddSeconds(-90));
        while (_waveReadSeries.Points.Count > 0 && _waveReadSeries.Points[0].X < cut)
            _waveReadSeries.Points.RemoveAt(0);
        while (_waveWriteSeries.Points.Count > 0 && _waveWriteSeries.Points[0].X < cut)
            _waveWriteSeries.Points.RemoveAt(0);
    }

    private static void RebuildPie(PlotModel model, DataGrid listView, IReadOnlyList<ProcessAggregate> items)
    {
        PieChart.Fill(model, items);
        long total = items.Sum(i => i.Bytes);
        var rows = new List<PieListRow>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double percent = total > 0 ? item.Bytes * 100.0 / total : 0;
            rows.Add(new PieListRow
            {
                Name = item.Name,
                BytesText = FormatUtil.Bytes(item.Bytes),
                PercentText = $"{percent:0.#}%",
                ColorBrush = PieChart.GetBrush(i),
            });
        }
        listView.ItemsSource = rows;
    }

    /// <summary>双击进程列表中的 System 行，在右侧栏打开“按文件 / 按磁盘”细分。</summary>
    private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
            return;

        // 用命中的行而不是 SelectedItem：进程列表每秒重建，选中项可能在两次点击之间被清掉。
        if (ItemsControl.ContainerFromElement(grid, source) is not DataGridRow row)
            return;
        if (row.Item is not PieListRow item)
            return;
        if (!string.Equals(item.Name, "System", StringComparison.OrdinalIgnoreCase))
            return;

        ShowSystemDetail();
    }

    private void SystemDetailButton_Click(object sender, RoutedEventArgs e) => ShowSystemDetail();

    /// <summary>打开右侧栏的 System（PID 4）按文件 / 按磁盘细分。</summary>
    private void ShowSystemDetail()
    {
        if (!_monitor.IsRecording && !_monitor.HasSystemIo)
            return;

        _sidebarHistoryEntry = null;
        SystemDetailSidebar.Refresh(_monitor);
        SystemDetailSidebar.Visibility = Visibility.Visible;
        HistoryDetailSidebar.Visibility = Visibility.Collapsed;
        SidebarTitleText.Text = L.T("System 细分", "System Breakdown");
        _sidebarMode = SidebarMode.System;
        OpenSidebar();
    }

    private void UpdateSummary()
    {
        TotalReadText.Text = FormatUtil.Bytes(_monitor.TotalReadBytes);
        TotalWriteText.Text = FormatUtil.Bytes(_monitor.TotalWriteBytes);
    }

    private void UpdateRecordTime()
    {
        RecordTimeText.Text = _monitor.IsRecording
            ? L.T("已记录 ", "Elapsed ") + FormatUtil.Duration(DateTime.Now - _recordingStartedAt)
            : "";
    }

    private void UnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;

        FormatUtil.Unit = UnitCombo.SelectedIndex switch
        {
            1 => ByteUnit.B,
            2 => ByteUnit.KB,
            3 => ByteUnit.MB,
            4 => ByteUnit.GB,
            _ => ByteUnit.Auto,
        };

        RateReadText.Text = FormatUtil.Bytes(_lastRateRead) + "/s";
        RateWriteText.Text = FormatUtil.Bytes(_lastRateWrite) + "/s";
        UpdateSummary();
        RebuildPies();
        RefreshHistoryList();
        // 侧栏里的字节数也依赖当前单位，立即重算。
        if (_sidebarMode == SidebarMode.History && _sidebarHistoryEntry != null)
            HistoryDetailSidebar.ShowEntry(_sidebarHistoryEntry, DeleteHistoryEntry);
        else if (_sidebarMode == SidebarMode.System)
            SystemDetailSidebar.Refresh(_monitor);
        _waveModel.InvalidatePlot(true);
        _lineModel.InvalidatePlot(true);
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;
        AppSettings.Language = LangCombo.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Chinese;
        AppSettings.Save();
        ApplyLanguage();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;
        AppSettings.Theme = ThemeCombo.SelectedIndex == 1 ? AppTheme.Dark : AppTheme.Light;
        AppSettings.Save();
        ApplyTheme();
    }

    private void RefreshHistoryList()
    {
        var rows = _history
            .OrderByDescending(h => h.StartTime)
            .Select(h => new HistoryRow
            {
                Entry = h,
                StartText = h.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                DurationText = FormatUtil.Duration(h.Duration),
                ReadText = FormatUtil.Bytes(h.ReadBytes),
                WriteText = FormatUtil.Bytes(h.WriteBytes),
            })
            .ToList();
        HistoryList.ItemsSource = rows;
    }

    /// <summary>点击历史记录行：在主窗口右侧栏查看该次会话的详情（不再弹新窗口）。</summary>
    private void HistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
            return;

        // 用命中的行而不是 SelectedItem，这样“已选中的行”再点一次也能重新展开侧栏。
        if (ItemsControl.ContainerFromElement(grid, source) is not DataGridRow row)
            return;
        if (row.Item is HistoryRow historyRow && historyRow.Entry != null)
            ShowHistoryDetail(historyRow.Entry);
    }

    private void ShowHistoryDetail(HistoryEntry entry)
    {
        _sidebarHistoryEntry = entry;
        HistoryDetailSidebar.ShowEntry(entry, DeleteHistoryEntry);
        HistoryDetailSidebar.Visibility = Visibility.Visible;
        SystemDetailSidebar.Visibility = Visibility.Collapsed;
        SidebarTitleText.Text = L.T("历史记录详情", "History Detail");
        _sidebarMode = SidebarMode.History;
        OpenSidebar();
    }

    private void OpenSidebar()
    {
        // 侧栏以覆盖式展开，左侧图表/列表的尺寸与布局完全不变；点右上角 ✕ 收起。
        SidebarPanel.Visibility = Visibility.Visible;
    }

    private void CloseSidebar()
    {
        if (_sidebarMode == SidebarMode.Hidden)
            return;

        SidebarPanel.Visibility = Visibility.Collapsed;
        HistoryDetailSidebar.Visibility = Visibility.Collapsed;
        SystemDetailSidebar.Visibility = Visibility.Collapsed;
        _sidebarMode = SidebarMode.Hidden;
    }

    private void SidebarCloseButton_Click(object sender, RoutedEventArgs e) => CloseSidebar();

    /// <summary>Esc 收起侧栏，不打断左侧正在看的图表。</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _sidebarMode != SidebarMode.Hidden)
        {
            CloseSidebar();
            e.Handled = true;
        }
    }

    private void DeleteHistoryEntry(HistoryEntry entry)
    {
        _history.Remove(entry);
        HistoryStore.Save(_history);
        if (_sidebarHistoryEntry == entry)
        {
            _sidebarHistoryEntry = null;
            CloseSidebar();
        }
        RefreshHistoryList();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0)
            return;
        if (MessageBox.Show(this, L.T("确定清空全部历史记录？", "Clear all history?"),
                L.T("清空历史", "Clear History"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _history.Clear();
            HistoryStore.Save(_history);
            if (_sidebarMode == SidebarMode.History)
            {
                _sidebarHistoryEntry = null;
                CloseSidebar();
            }
            RefreshHistoryList();
        }
    }

    private void UpdateUi()
    {
        bool recording = _monitor.IsRecording;
        StartButton.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        SystemDetailButton.IsEnabled = recording || _monitor.HasSystemIo;
        StatusText.Text = recording ? L.T("● 正在记录", "● Recording") : L.T("○ 已停止", "○ Stopped");
        bool dark = AppSettings.Theme == AppTheme.Dark;
        StatusText.Foreground = recording
            ? (dark ? Brushes.LightGreen : Brushes.ForestGreen)
            : (Brush)FindResource("AppForeground");
        UpdateRecordTime();
    }

    private void OnMonitorError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _timer.Stop();
            UpdateUi();
            MessageBox.Show(this, L.T("记录过程中出错：\n\n", "Error while recording:\n\n") + message,
                L.T("记录已停止", "Recording Stopped"), MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void ApplyLanguage()
    {
        Title = L.T("磁盘 I/O 监控器 (ETW)", "Disk I/O Monitor (ETW)");
        StartButton.Content = L.T("▶ 开始记录", "▶ Start");
        StopButton.Content = L.T("■ 停止记录", "■ Stop");
        ResetButton.Content = L.T("↺ 重置统计", "↺ Reset");
        SystemDetailButton.Content = L.T("System 细分", "System Details");
        SystemDetailButton.ToolTip = L.T("在右侧栏按文件 / 按磁盘查看 System（PID 4）的内核 I/O 细分",
            "View System (PID 4) kernel I/O by file / physical disk in the side panel");
        ClearHistoryButton.Content = L.T("清空全部", "Clear All");

        TotalReadLabel.Text = L.T("累计读取：", "Total Read: ");
        TotalWriteLabel.Text = L.T("累计写入：", "Total Write: ");
        RateReadLabel.Text = L.T("当前速率（读）：", "Rate (Read): ");
        RateWriteLabel.Text = L.T("当前速率（写）：", "Rate (Write): ");
        UnitLabel.Text = L.T("单位：", "Unit: ");
        LangLabel.Text = L.T("语言：", "Language: ");
        ThemeLabel.Text = L.T("主题：", "Theme: ");
        ThemeLightItem.Content = L.T("白天", "Light");
        ThemeDarkItem.Content = L.T("夜间", "Dark");
        ((ComboBoxItem)UnitCombo.Items[0]).Content = L.T("自动", "Auto");
        HistoryHeaderText.Text = L.T("历史记录（点击在右侧查看详情）", "History (click to view details)");
        SidebarCloseButton.ToolTip = L.T("关闭侧栏", "Close panel");
        SidebarTitleText.Text = _sidebarMode switch
        {
            SidebarMode.History => L.T("历史记录详情", "History Detail"),
            SidebarMode.System => L.T("System 细分", "System Breakdown"),
            _ => L.T("详情", "Details"),
        };
        HistoryDetailSidebar.ApplyLanguage();
        SystemDetailSidebar.ApplyLanguage();

        _waveModel.Title = L.T("磁盘读写速率（波形）", "Disk Read/Write Rate");
        _lineModel.Title = L.T("磁盘读写累计字节数", "Cumulative Disk Bytes");
        _pieReadModel.Title = L.T("读取分布", "Read Distribution");
        _pieWriteModel.Title = L.T("写入分布", "Write Distribution");

        _waveReadSeries.Title = L.T("读取速率", "Read Rate");
        _waveWriteSeries.Title = L.T("写入速率", "Write Rate");
        _readSeries.Title = L.T("读取 (Read)", "Read");
        _writeSeries.Title = L.T("写入 (Write)", "Write");

        ((DateTimeAxis)_waveModel.Axes[0]).Title = L.T("时间", "Time");
        ((LinearAxis)_waveModel.Axes[1]).Title = L.T("字节/秒", "Bytes/sec");
        ((DateTimeAxis)_lineModel.Axes[0]).Title = L.T("时间", "Time");
        ((LinearAxis)_lineModel.Axes[1]).Title = L.T("字节数", "Bytes");

        SetHeaders3(ReadList, L.T("进程", "Process"), L.T("字节", "Bytes"), L.T("占比", "Share"));
        SetHeaders3(WriteList, L.T("进程", "Process"), L.T("字节", "Bytes"), L.T("占比", "Share"));
        SetHeaders4(HistoryList, L.T("开始时间", "Start Time"), L.T("时长", "Duration"), L.T("读取", "Read"), L.T("写入", "Write"));

        ReadList.ToolTip = L.T("双击 System 行查看按文件 / 按磁盘细分",
            "Double-click a System row for per-file / per-disk breakdown");
        WriteList.ToolTip = ReadList.ToolTip;

        _waveModel.InvalidatePlot(true);
        _lineModel.InvalidatePlot(true);
        _pieReadModel.InvalidatePlot(true);
        _pieWriteModel.InvalidatePlot(true);

        // 侧栏里带语言标签的文字（“未解析文件名”“其他文件”等）立即重算。
        if (_sidebarMode == SidebarMode.System)
            SystemDetailSidebar.Refresh(_monitor);

        UpdateUi();
    }

    private void ApplyTheme()
    {
        bool dark = AppSettings.Theme == AppTheme.Dark;
        var res = Application.Current.Resources;

        res["AppBackground"] = new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
        res["AppForeground"] = new SolidColorBrush(dark ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Colors.Black);
        res["ControlBackground"] = new SolidColorBrush(dark ? Color.FromRgb(0x25, 0x25, 0x26) : Colors.White);
        res["ControlForeground"] = new SolidColorBrush(dark ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Colors.Black);
        res["BorderColor"] = new SolidColorBrush(dark ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0xD3, 0xD3, 0xD3));
        res["HeaderBackground"] = new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0x2D, 0x30) : Color.FromRgb(0xF0, 0xF0, 0xF0));

        var chartBg = ThemePalette.ChartBackground();
        var chartText = ThemePalette.ChartText();
        foreach (var m in new[] { _waveModel, _lineModel, _pieReadModel, _pieWriteModel })
        {
            m.Background = chartBg;
            m.TextColor = chartText;
            m.InvalidatePlot(true);
        }
        HistoryDetailSidebar.ApplyTheme();
        SystemDetailSidebar.ApplyTheme();

        UpdateUi();
    }

    private static void SetHeaders3(DataGrid grid, string h1, string h2, string h3)
    {
        grid.Columns[0].Header = h1;
        grid.Columns[1].Header = h2;
        grid.Columns[2].Header = h3;
    }

    private static void SetHeaders4(DataGrid grid, string h1, string h2, string h3, string h4)
    {
        grid.Columns[0].Header = h1;
        grid.Columns[1].Header = h2;
        grid.Columns[2].Header = h3;
        grid.Columns[3].Header = h4;
    }

    private PlotModel BuildRateModel()
    {
        var model = new PlotModel
        {
            Title = L.T("磁盘读写速率（波形）", "Disk Read/Write Rate"),
            TextColor = ThemePalette.ChartText(),
            Background = ThemePalette.ChartBackground(),
        };
        model.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.TopRight,
            LegendOrientation = LegendOrientation.Horizontal,
            LegendPlacement = LegendPlacement.Inside,
        });
        _waveAxis.Title = L.T("时间", "Time");
        model.Axes.Add(_waveAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = L.T("字节/秒", "Bytes/sec"), LabelFormatter = v => FormatUtil.Bytes(v) + "/s" });

        _waveReadSeries.Title = L.T("读取速率", "Read Rate");
        _waveReadSeries.Color = OxyColors.DodgerBlue;
        _waveReadSeries.StrokeThickness = 1.5;
        _waveWriteSeries.Title = L.T("写入速率", "Write Rate");
        _waveWriteSeries.Color = OxyColors.OrangeRed;
        _waveWriteSeries.StrokeThickness = 1.5;

        model.Series.Add(_waveReadSeries);
        model.Series.Add(_waveWriteSeries);
        return model;
    }

    private PlotModel BuildCumulativeModel()
    {
        var model = new PlotModel
        {
            Title = L.T("磁盘读写累计字节数", "Cumulative Disk Bytes"),
            TextColor = ThemePalette.ChartText(),
            Background = ThemePalette.ChartBackground(),
        };
        model.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.TopRight,
            LegendOrientation = LegendOrientation.Horizontal,
            LegendPlacement = LegendPlacement.Inside,
        });
        model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, Title = L.T("时间", "Time"), StringFormat = "HH:mm:ss" });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = L.T("字节数", "Bytes"), LabelFormatter = v => FormatUtil.Bytes(v) });

        _readSeries.Title = L.T("读取 (Read)", "Read");
        _readSeries.Color = OxyColors.DodgerBlue;
        _readSeries.StrokeThickness = 2;
        _writeSeries.Title = L.T("写入 (Write)", "Write");
        _writeSeries.Color = OxyColors.OrangeRed;
        _writeSeries.StrokeThickness = 2;

        model.Series.Add(_readSeries);
        model.Series.Add(_writeSeries);
        return model;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (_monitor.IsRecording)
        {
            _monitor.Stop();
            _history.Add(BuildHistoryEntry());
            HistoryStore.Save(_history);
        }
        _monitor.Dispose();
        base.OnClosed(e);
    }
}

internal sealed class PieListRow
{
    public string Name { get; set; } = "";
    public string BytesText { get; set; } = "";
    public string PercentText { get; set; } = "";
    public Brush? ColorBrush { get; set; }
}

internal sealed class HistoryRow
{
    public HistoryEntry? Entry { get; set; }
    public string StartText { get; set; } = "";
    public string DurationText { get; set; } = "";
    public string ReadText { get; set; } = "";
    public string WriteText { get; set; } = "";
}

/// <summary>主窗口右侧栏当前展示的内容。</summary>
internal enum SidebarMode
{
    Hidden,
    History,
    System,
}
