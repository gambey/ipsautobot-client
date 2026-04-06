using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public sealed class StopPlatformOrderingSettingsService : IStopPlatformOrderingSettingsService
{
    public string FilePath { get; } = Path.Combine(
        ClientSettingsPaths.ClientSettingsDirectory,
        "stopPlatformOrdering.json");

    private static string LegacyFilePath => Path.Combine(
        ClientSettingsPaths.LegacyAppDirectory,
        "stopPlatformOrdering.json");

    public StopPlatformOrderingSettingsData Load()
    {
        try
        {
            var path = ClientSettingsPaths.ResolveExistingPath(FilePath, LegacyFilePath);
            if (path == null)
                return new StopPlatformOrderingSettingsData();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StopPlatformOrderingSettingsData>(json, ClientSettingsJson.DeserializeOptions)
                   ?? new StopPlatformOrderingSettingsData();
        }
        catch
        {
            return new StopPlatformOrderingSettingsData();
        }
    }

    public void Save(StopPlatformOrderingSettingsData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data, ClientSettingsJson.SerializeOptions);
        File.WriteAllText(FilePath, json);
    }
}
