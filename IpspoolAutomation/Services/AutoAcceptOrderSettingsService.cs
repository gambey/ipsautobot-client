using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;
using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public sealed class AutoAcceptOrderSettingsService : IAutoAcceptOrderSettingsService
{
    public string FilePath { get; } = Path.Combine(
        ClientSettingsPaths.ClientSettingsDirectory,
        "autoAcceptOrder.json");

    private static string LegacyFilePath => Path.Combine(
        ClientSettingsPaths.LegacyAppDirectory,
        "autoAcceptOrder.json");

    public AutoAcceptOrderSettingsData Load()
    {
        try
        {
            var path = ClientSettingsPaths.ResolveExistingPath(FilePath, LegacyFilePath);
            if (path == null)
                return new AutoAcceptOrderSettingsData();

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<AutoAcceptOrderSettingsData>(json, ClientSettingsJson.DeserializeOptions)
                       ?? new AutoAcceptOrderSettingsData();
            if (data.NavigationSteps.Count == 0 && data.Steps is { Count: > 0 })
                data.NavigationSteps = new List<CaptureTargetItem>(data.Steps);
            return data;
        }
        catch
        {
            return new AutoAcceptOrderSettingsData();
        }
    }

    public void Save(AutoAcceptOrderSettingsData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data, ClientSettingsJson.SerializeOptions);
        File.WriteAllText(FilePath, json);
    }
}
