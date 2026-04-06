using CommunityToolkit.Mvvm.ComponentModel;

namespace IpspoolAutomation.Models;

/// <summary>已接单记录（表格与汇总用）。</summary>
public sealed class AcceptedOrderRow : ObservableObject
{
    private string _username = "";
    private string _orderNumber = "";
    private decimal _unitPrice;
    private decimal _highDiff;
    private decimal _highDiffRatioPercent;
    private decimal _avgDiff;
    private decimal _avgDiffRatioPercent;

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
}
