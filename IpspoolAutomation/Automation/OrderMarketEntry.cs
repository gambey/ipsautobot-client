namespace IpspoolAutomation.Automation;

/// <summary>订单市场单行解析结果（已通过退款率过滤）。</summary>
public sealed record OrderMarketEntry(
    string OrderId,
    decimal UnitPrice,
    decimal RefundRatePercent,
    int PageIndex,
    int ItemIndex);
