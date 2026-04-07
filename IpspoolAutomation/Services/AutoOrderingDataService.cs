using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public sealed class AutoOrderingDataService : IAutoOrderingDataService
{
    public const int MaxRecords = 100;

    public string FilePath { get; } = Path.Combine(
        ClientSettingsPaths.LegacyAppDirectory,
        "autoOrderingData.json");

    public AutoOrderingDataFile Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AutoOrderingDataFile();
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<AutoOrderingDataFile>(json, ClientSettingsJson.DeserializeOptions)
                       ?? new AutoOrderingDataFile();
            data.Items = data.Items
                .OrderByDescending(x => x.RecordedAt)
                .Take(MaxRecords)
                .ToList();
            return data;
        }
        catch
        {
            return new AutoOrderingDataFile();
        }
    }

    public void Save(AutoOrderingDataFile data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        data.Items = data.Items
            .OrderByDescending(x => x.RecordedAt)
            .Take(MaxRecords)
            .ToList();
        var json = JsonSerializer.Serialize(data, ClientSettingsJson.SerializeOptions);
        File.WriteAllText(FilePath, json);
    }
}
