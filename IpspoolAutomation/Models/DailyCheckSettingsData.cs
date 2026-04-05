namespace IpspoolAutomation.Models;

public sealed class DailyCheckSettingsData
{
    public string Mode { get; set; } = "Cancel";
    public string StartTime { get; set; } = "07:00";

    /// <summary>最近一次「定时自动签到」完成的本地日历日（yyyy-MM-dd）。手动立刻签到不写入。</summary>
    public string? LastAutoCheckinDate { get; set; }
}
