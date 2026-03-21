using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IWithdrawDailyService
{
    string FilePath { get; }
    WithdrawDailyData Load();
    void Save(WithdrawDailyData data);
}
