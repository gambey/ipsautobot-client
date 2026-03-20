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
}

