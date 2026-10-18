using OxyPlot;
using OxyPlot.Series;

namespace DiskMonitor;

/// <summary>
/// 带自定义悬停提示的饼图系列：
/// 饼图内仅对“够大”的扇区显示标签（小扇区标签为空，避免文字重叠）；
/// 鼠标悬停（Tracker）时始终显示完整信息（名字 + 字节 + 占比）。
/// </summary>
internal sealed class TooltipPieSeries : PieSeries
{
    private readonly Dictionary<PieSlice, string> _names = new();

    public void ClearSlices()
    {
        Slices.Clear();
        _names.Clear();
    }

    /// <param name="label">饼图内显示的标签（小扇区传空字符串即不显示）。</param>
    /// <param name="name">完整进程名（用于悬停提示）。</param>
    /// <param name="value">字节数。</param>
    /// <param name="color">扇区填充色（与列表色块一致）。</param>
    public void AddSlice(string label, string name, double value, OxyColor color)
    {
        var slice = new PieSlice(label, value) { Fill = color };
        _names[slice] = name;
        Slices.Add(slice);
    }

    public override TrackerHitResult? GetNearestPoint(ScreenPoint point, bool interpolate)
    {
        var result = base.GetNearestPoint(point, interpolate);
        if (result?.Item is PieSlice slice && _names.TryGetValue(slice, out var name))
        {
            double total = Slices.Sum(s => s.Value);
            double percent = total > 0 ? slice.Value / total : 0;
            result.Text = $"{name}\n{FormatUtil.Bytes(slice.Value)}  ({percent:P1})";
        }
        return result;
    }
}
