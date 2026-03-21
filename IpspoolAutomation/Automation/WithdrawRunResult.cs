using IpspoolAutomation.Models;

namespace IpspoolAutomation.Automation;

public sealed record WithdrawRunResult(
    int ProcessedCount,
    long TotalCoins,
    double ElapsedSeconds,
    IReadOnlyList<WithdrawDetailItem> Details);
