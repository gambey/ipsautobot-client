using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public sealed class DailyCheckExeService : IDailyCheckExeService
{
    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "dailyCheckExe.json");

    public CaptureTargetSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new CaptureTargetSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<CaptureTargetSettings>(json) ?? new CaptureTargetSettings();
        }
        catch
        {
            return new CaptureTargetSettings();
        }
    }

    public void Save(CaptureTargetSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
