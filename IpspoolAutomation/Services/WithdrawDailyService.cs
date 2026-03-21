using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public sealed class WithdrawDailyService : IWithdrawDailyService
{
    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "withdraw_daily.json");

    public WithdrawDailyData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new WithdrawDailyData();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WithdrawDailyData>(json) ?? new WithdrawDailyData();
        }
        catch
        {
            return new WithdrawDailyData();
        }
    }

    public void Save(WithdrawDailyData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
