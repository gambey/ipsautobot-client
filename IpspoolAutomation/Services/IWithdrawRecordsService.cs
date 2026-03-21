using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IWithdrawRecordsService
{
    string FilePath { get; }

    WithdrawRecordsData Load();
    void Save(WithdrawRecordsData data);
}
