using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IpspoolAutomation.Models;

/// <summary>单次提现详情表格行（与自动化计算一致：手续费 = 兑换积分 × withdrawFeeRatio）。</summary>
public sealed class WithdrawDetailItem : INotifyPropertyChanged
{
    private int _rowIndex;

    /// <summary>展示序号（1-based），与当前列表顺序一致；写入 withdraw_daily.json。</summary>
    public int RowIndex
    {
        get => _rowIndex;
        set
        {
            if (_rowIndex == value)
                return;
            _rowIndex = value;
            OnPropertyChanged();
        }
    }

    public string Account { get; set; } = "";

    /// <summary>兑换积分（讯币对应积分消耗，不含单独展示的手续费列时即为 coinCostPoints）。</summary>
    public int Points { get; set; }

    public int RemainingPoints { get; set; }

    public decimal ExchangedCoins { get; set; }

    /// <summary>手续费（积分），与 Points 同量纲。</summary>
    public int Fee { get; set; }

    public string Status { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
