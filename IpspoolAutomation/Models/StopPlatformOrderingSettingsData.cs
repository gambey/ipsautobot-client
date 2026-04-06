using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Models;

/// <summary>停止平台单配置（<c>stopPlatformOrdering.json</c>）。</summary>
public sealed class StopPlatformOrderingSettingsData
{
    /// <summary>单次流程最多处理的辅助端账号数（默认 60）。</summary>
    public int StopAccountCount { get; set; } = 60;

    public List<CaptureTargetItem> CaptureTargetList { get; set; } = new();
}
