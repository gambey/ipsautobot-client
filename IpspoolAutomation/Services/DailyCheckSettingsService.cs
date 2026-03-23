using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public sealed class DailyCheckSettingsService : IDailyCheckSettingsService
{
    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "dailyCheckSettings.json");

    public DailyCheckSettingsData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new DailyCheckSettingsData();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<DailyCheckSettingsData>(json) ?? new DailyCheckSettingsData();
        }
        catch
        {
            return new DailyCheckSettingsData();
        }
    }

    public void Save(DailyCheckSettingsData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
