namespace IpspoolAutomation.Models.Capture;

public sealed class CaptureTargetItem
{
    public int TargetID { get; set; }
    public string TargetType { get; set; } = "";
    public string TargetText { get; set; } = "";
    public string? AnchorType { get; set; }
    public string? AnchorText { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public string Action { get; set; } = "click";
    public string? InputValue { get; set; }

    /// <summary>该步执行完成后等待的毫秒数；未配置时由客户端默认 300。</summary>
    public int? DelayMs { get; set; }

    /// <summary>配置备注，仅用于标记说明，不参与执行逻辑。</summary>
    public string? Remark { get; set; }
}

