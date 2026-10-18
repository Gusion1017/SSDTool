using System.Windows.Media;
using OxyPlot;
using OxyPlot.Series;

namespace DiskMonitor;

/// <summary>饼图构建/填充的共用逻辑（主窗口与历史详情窗口共用），含柔和配色。</summary>
internal static class PieChart
{
    public const double LabelThresholdPercent = 4.0; // 占比低于该值的扇区不在饼图内显示名字

    // 柔和配色（低饱和、中等亮度），饼图扇区与右侧列表行背景按相同索引对应。
    private static readonly OxyColor[] Palette =
    {
        OxyColor.FromRgb(0x74, 0x96, 0xC1), // 柔和蓝
        OxyColor.FromRgb(0x7E, 0xA9, 0x7E), // 柔和绿
        OxyColor.FromRgb(0xD0, 0x9E, 0x72), // 柔和橙
        OxyColor.FromRgb(0xA0, 0x8E, 0xC0), // 柔和紫
        OxyColor.FromRgb(0x6E, 0xA8, 0xAE), // 柔和青
        OxyColor.FromRgb(0xC0, 0x8E, 0x9E), // 柔和粉
        OxyColor.FromRgb(0x9E, 0xB0, 0x72), // 柔和橄榄
        OxyColor.FromRgb(0xC0, 0xA8, 0x6E), // 柔和金
        OxyColor.FromRgb(0x7A, 0xA0, 0xC8), // 柔和天蓝
        OxyColor.FromRgb(0x6E, 0xBE, 0xA0), // 柔和薄荷
        OxyColor.FromRgb(0xC0, 0x8E, 0xA0), // 柔和玫瑰
        OxyColor.FromRgb(0x8A, 0x9A, 0xAE), // 柔和灰蓝
        OxyColor.FromRgb(0xD0, 0xB0, 0x7E), // 柔和杏黄
        OxyColor.FromRgb(0xAE, 0x96, 0xC8), // 柔和薰衣草
        OxyColor.FromRgb(0xB0, 0x98, 0x7E), // 柔和浅棕
        OxyColor.FromRgb(0x9A, 0x9A, 0x9A), // 柔和灰
    };

    public static PlotModel BuildModel(string title)
    {
        var model = new PlotModel
        {
            Title = title,
            TextColor = ThemePalette.ChartText(),
            Background = ThemePalette.ChartBackground(),
        };
        var series = new TooltipPieSeries
        {
            StrokeThickness = 1,
            InsideLabelFormat = "{1}",
            InsideLabelPosition = 0.55,
            AngleSpan = 360,
            StartAngle = 0,
        };
        model.Series.Add(series);
        return model;
    }

    /// <summary>返回调色板中第 index 个扇区颜色（超出长度循环）。</summary>
    public static OxyColor ColorAt(int index) => Palette[index % Palette.Length];

    public static void Fill(PlotModel model, IReadOnlyList<ProcessAggregate> items)
    {
        var series = (TooltipPieSeries)model.Series[0];
        series.ClearSlices();
        long total = items.Sum(i => i.Bytes);
        int i = 0;
        foreach (var item in items)
        {
            double percent = total > 0 ? item.Bytes * 100.0 / total : 0;
            string label = percent >= LabelThresholdPercent ? item.Name : "";
            series.AddSlice(label, item.Name, item.Bytes, ColorAt(i));
            i++;
        }
        model.InvalidatePlot(true);
    }

    /// <summary>返回与饼图扇区相同颜色的 WPF 画刷（供列表整行背景使用）。</summary>
    public static Brush GetBrush(int index)
    {
        var c = ColorAt(index);
        return new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
    }
}
