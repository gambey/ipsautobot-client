namespace IpspoolAutomation.Models;

public sealed class WithdrawDailyData
{
    public List<WithdrawDetailItem> Items { get; set; } = new();
}
