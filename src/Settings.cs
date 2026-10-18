using System.IO;
using System.Text.Json;
using OxyPlot;

namespace DiskMonitor;

internal enum AppLanguage { Chinese, English }
internal enum AppTheme { Light, Dark }

/// <summary>应用设置（语言、主题），持久化到本地 JSON。</summary>
internal static class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskMonitor", "settings.json");

    public static AppLanguage Language = AppLanguage.Chinese;
    public static AppTheme Theme = AppTheme.Light;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath));
                if (d != null)
                {
                    Language = d.Language == "en" ? AppLanguage.English : AppLanguage.Chinese;
                    Theme = d.Theme == "dark" ? AppTheme.Dark : AppTheme.Light;
                }
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new SettingsData
            {
                Language = Language == AppLanguage.English ? "en" : "zh",
                Theme = Theme == AppTheme.Dark ? "dark" : "light",
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed class SettingsData
    {
        public string Language { get; set; } = "zh";
        public string Theme { get; set; } = "light";
    }
}

/// <summary>多语言文本辅助：L.T(中文, English)。</summary>
internal static class L
{
    public static string T(string zh, string en) =>
        AppSettings.Language == AppLanguage.Chinese ? zh : en;
}

/// <summary>图表配色（随主题变化）。</summary>
internal static class ThemePalette
{
    public static OxyColor ChartBackground() =>
        AppSettings.Theme == AppTheme.Dark ? OxyColor.FromRgb(0x1E, 0x1E, 0x1E) : OxyColors.White;

    public static OxyColor ChartText() =>
        AppSettings.Theme == AppTheme.Dark ? OxyColor.FromRgb(0xE0, 0xE0, 0xE0) : OxyColors.Black;
}
