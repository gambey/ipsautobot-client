using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IpspoolAutomation.Models;

/// <summary>已接单记录（表格与汇总用）。</summary>
public sealed class AcceptedOrderRow : ObservableObject
{
    private int _rowIndex;
    private DateTime _recordedAt;
    private string _username = "";
    private string _orderNumber = "";
    private decimal _unitPrice;
    private decimal _highDiff;
    private decimal _highDiffRatioPercent;
    private decimal _avgDiff;
    private decimal _avgDiffRatioPercent;

    /// <summary>表格序号（1 起，时间降序时最新为 1）。</summary>
    public int RowIndex
    {
        get => _rowIndex;
        set => SetProperty(ref _rowIndex, value);
    }

    /// <summary>签约完成时间。</summary>
    public DateTime RecordedAt
    {
        get => _recordedAt;
        set
        {
            if (SetProperty(ref _recordedAt, value))
                OnPropertyChanged(nameof(TimeDisplay));
        }
    }

    public string TimeDisplay =>
        _recordedAt == default
            ? ""
            : _recordedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set => SetProperty(ref _orderNumber, value);
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set => SetProperty(ref _unitPrice, value);
    }

    public decimal HighDiff
    {
        get => _highDiff;
        set => SetProperty(ref _highDiff, value);
    }

    /// <summary>百分比数值（如 12.34 表示 12.34%）。</summary>
    public decimal HighDiffRatioPercent
    {
        get => _highDiffRatioPercent;
        set => SetProperty(ref _highDiffRatioPercent, value);
    }

    public decimal AvgDiff
    {
        get => _avgDiff;
        set => SetProperty(ref _avgDiff, value);
    }

    public decimal AvgDiffRatioPercent
    {
        get => _avgDiffRatioPercent;
        set => SetProperty(ref _avgDiffRatioPercent, value);
    }

    public static AcceptedOrderRow FromPersisted(AutoOrderingPersistedRecord r)
    {
        return new AcceptedOrderRow
        {
            RecordedAt = r.RecordedAt,
            Username = r.Username ?? "",
            OrderNumber = r.OrderNumber ?? "",
            UnitPrice = r.UnitPrice,
            HighDiff = r.HighDiff,
            HighDiffRatioPercent = r.HighDiffRatioPercent,
            AvgDiff = r.AvgDiff,
            AvgDiffRatioPercent = r.AvgDiffRatioPercent
        };
    }

    public AutoOrderingPersistedRecord ToPersisted() => new()
    {
        RecordedAt = RecordedAt,
        Username = Username,
        OrderNumber = OrderNumber,
        UnitPrice = UnitPrice,
        HighDiff = HighDiff,
        HighDiffRatioPercent = HighDiffRatioPercent,
        AvgDiff = AvgDiff,
        AvgDiffRatioPercent = AvgDiffRatioPercent
    };
}
