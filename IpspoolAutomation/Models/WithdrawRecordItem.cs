using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IpspoolAutomation.Models;

public sealed class WithdrawRecordItem : INotifyPropertyChanged
{
    private int _rowIndex;

    /// <summary>展示序号（1-based），与当前列表顺序一致；写入 withdraw_records.json。</summary>
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

    public DateTime RecordedAt { get; set; } = DateTime.Now;

    public int ProcessedAccountCount { get; set; }

    /// <summary>本次提现涉及的讯币合计。</summary>
    public decimal Amount { get; set; }

    [JsonIgnore]
    public string TimeDisplay => RecordedAt.ToString("MM-dd-yyyy hh:mm", CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
