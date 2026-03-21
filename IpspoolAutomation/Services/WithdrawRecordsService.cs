using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public sealed class WithdrawRecordsService : IWithdrawRecordsService
{
    public const int MaxRecords = 10;

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "withdraw_records.json");

    public WithdrawRecordsData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new WithdrawRecordsData();
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<WithdrawRecordsData>(json);
            return data ?? new WithdrawRecordsData();
        }
        catch
        {
            return new WithdrawRecordsData();
        }
    }

    public void Save(WithdrawRecordsData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
