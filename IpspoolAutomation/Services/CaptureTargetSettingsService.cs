using System.Text.Json;
using System.IO;
using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public sealed class CaptureTargetSettingsService : ICaptureTargetSettingsService
{
    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "targetSettings.json");

    public CaptureTargetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new CaptureTargetSettings();

            var json = File.ReadAllText(SettingsPath);
            var data = JsonSerializer.Deserialize<CaptureTargetSettings>(json);
            return data ?? new CaptureTargetSettings();
        }
        catch
        {
            return new CaptureTargetSettings();
        }
    }

    public void Save(CaptureTargetSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(SettingsPath, json);
    }
}

