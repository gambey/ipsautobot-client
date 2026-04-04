using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public sealed class ExchangeScoreSettingsService : IExchangeScoreSettingsService
{
    public string FilePath { get; } = Path.Combine(
        ClientSettingsPaths.ClientSettingsDirectory,
        "exchange_score.json");

    private static string LegacyFilePath => Path.Combine(
        ClientSettingsPaths.LegacyAppDirectory,
        "exchange_score.json");

    public CaptureTargetSettings Load()
    {
        try
        {
            var path = ClientSettingsPaths.ResolveExistingPath(FilePath, LegacyFilePath);
            if (path == null)
                return new CaptureTargetSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CaptureTargetSettings>(json, ClientSettingsJson.DeserializeOptions)
                   ?? new CaptureTargetSettings();
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
        var json = JsonSerializer.Serialize(settings, ClientSettingsJson.SerializeOptions);
        File.WriteAllText(FilePath, json);
    }
}
