using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public sealed class StartPlatformOrderingSettingsService : IStartPlatformOrderingSettingsService
{
    public string FilePath { get; } = Path.Combine(
        ClientSettingsPaths.ClientSettingsDirectory,
        "startPlatformOrdering.json");

    private static string LegacyFilePath => Path.Combine(
        ClientSettingsPaths.LegacyAppDirectory,
        "startPlatformOrdering.json");

    public StartPlatformOrderingSettingsData Load()
    {
        try
        {
            var path = ClientSettingsPaths.ResolveExistingPath(FilePath, LegacyFilePath);
            if (path == null)
                return new StartPlatformOrderingSettingsData();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StartPlatformOrderingSettingsData>(json, ClientSettingsJson.DeserializeOptions)
                   ?? new StartPlatformOrderingSettingsData();
        }
        catch
        {
            return new StartPlatformOrderingSettingsData();
        }
    }

    public void Save(StartPlatformOrderingSettingsData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data, ClientSettingsJson.SerializeOptions);
        File.WriteAllText(FilePath, json);
    }
}
