using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Models;

/// <summary>启用平台单配置（<c>startPlatformOrdering.json</c>）。</summary>
public sealed class StartPlatformOrderingSettingsData
{
    /// <summary>与停止平台单共用的单次操作账号数（默认 60）。</summary>
    public int OperationAccountCount { get; set; } = 60;

    public List<CaptureTargetItem> CaptureTargetList { get; set; } = new();
}
