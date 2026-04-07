namespace IpspoolAutomation.Models;

/// <summary>持久化文件 <c>autoOrderingData.json</c>（签约成功记录，最多 100 条）。</summary>
public sealed class AutoOrderingDataFile
{
    public List<AutoOrderingPersistedRecord> Items { get; set; } = new();
}

/// <summary>单条签约记录（与 <see cref="AcceptedOrderRow"/> 对应，可 JSON 序列化）。</summary>
public sealed class AutoOrderingPersistedRecord
{
    public DateTime RecordedAt { get; set; }

    public string Username { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal HighDiff { get; set; }
    public decimal HighDiffRatioPercent { get; set; }
    public decimal AvgDiff { get; set; }
    public decimal AvgDiffRatioPercent { get; set; }
}
