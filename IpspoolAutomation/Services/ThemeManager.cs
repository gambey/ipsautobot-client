using System.IO;
using System.Text.Json;
using System.Windows;

namespace IpspoolAutomation.Services;

public enum AppUiTheme
{
    Dark,
    Light
}

/// <summary>在 Application 合并字典中切换亮色/暗色主题（与 MainWindow 使用 DynamicResource 配合）。</summary>
public static class ThemeManager
{
    private const string DarkPackUri = "pack://application:,,,/Themes/DarkTheme.xaml";
    private const string LightPackUri = "pack://application:,,,/Themes/LightTheme.xaml";

    private static readonly string UiSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "ui-settings.json");

    /// <summary>启动时从本地 ui-settings.json 读取（无字段则暗色）。</summary>
    public static AppUiTheme ReadSavedTheme()
    {
        try
        {
            if (!File.Exists(UiSettingsPath))
                return AppUiTheme.Dark;
            using var doc = JsonDocument.Parse(File.ReadAllText(UiSettingsPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("uiTheme", out var camel) || root.TryGetProperty("UiTheme", out camel))
            {
                var s = camel.GetString();
                if (string.Equals(s, "Light", StringComparison.OrdinalIgnoreCase))
                    return AppUiTheme.Light;
            }
        }
        catch
        {
            // ignore
        }

        return AppUiTheme.Dark;
    }

    public static void Apply(AppUiTheme theme)
    {
        var app = Application.Current;
        if (app?.Resources.MergedDictionaries is not { } merged)
            return;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source;
            if (src == null)
                continue;
            var s = src.ToString();
            if (s.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }

        var uri = new Uri(theme == AppUiTheme.Light ? LightPackUri : DarkPackUri, UriKind.Absolute);
        merged.Insert(0, new ResourceDictionary { Source = uri });
    }
}
