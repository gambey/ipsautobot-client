using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Models;
using IpspoolAutomation.Models.Capture;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.ViewModels;

public sealed partial class AutoAcceptOrderSettingsViewModel : ObservableObject
{
    private readonly IAutoAcceptOrderSettingsService _service;
    private Action? _closeAction;

    [ObservableProperty] private string _pollIntervalMinutesText = "10";
    [ObservableProperty] private string _maxRefundRatePercentText = "100";
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<CaptureTargetItemRowViewModel> NavigationTargets { get; } = new();
    public ObservableCollection<CaptureTargetItemRowViewModel> SignTargets { get; } = new();
    public CaptureTargetItemRowViewModel NextPageRow { get; }

    public IReadOnlyList<string> TargetTypeOptions { get; } = new[] { "text", "inputBox", "button", "radioBtn", "dropList", "checkbox", "window", "dialog" };
    public IReadOnlyList<string> ActionOptions { get; } = new[] { "click", "select", "unselect", "moveTo_click", "moveTo_click_input", "click_select", "solve_math" };

    public AutoAcceptOrderSettingsViewModel(IAutoAcceptOrderSettingsService service)
    {
        _service = service;
        NextPageRow = new CaptureTargetItemRowViewModel { TargetID = 1 };
        var data = service.Load();
        PollIntervalMinutesText = Math.Max(1, data.PollIntervalMinutes).ToString();
        MaxRefundRatePercentText = data.MaxRefundRatePercent.ToString("0.##");
        LoadRows(NavigationTargets, data.NavigationSteps);
        LoadRows(SignTargets, data.SignOrderSteps);
        if (data.NextPageStep != null)
            ApplyItemToRow(data.NextPageStep, NextPageRow);
        else
            ClearRow(NextPageRow);
    }

    public void SetCloseAction(Action action) => _closeAction = action;

    private static void ClearRow(CaptureTargetItemRowViewModel row)
    {
        row.TargetType = "text";
        row.TargetText = "";
        row.AnchorType = "";
        row.AnchorText = "";
        row.OffsetX = "0";
        row.OffsetY = "0";
        row.Action = "click";
        row.InputValue = "";
        row.DelayMs = "300";
        row.Remark = "";
    }

    private static void LoadRows(ObservableCollection<CaptureTargetItemRowViewModel> dest, List<CaptureTargetItem> items)
    {
        dest.Clear();
        if (items.Count == 0)
        {
            dest.Add(CreateEmptyRow(1));
            return;
        }

        foreach (var item in items.OrderBy(x => x.TargetID))
        {
            var row = new CaptureTargetItemRowViewModel();
            ApplyItemToRow(item, row);
            dest.Add(row);
        }

        NormalizeIds(dest);
    }

    private static CaptureTargetItemRowViewModel CreateEmptyRow(int id) => new()
    {
        TargetID = id,
        TargetType = "text",
        AnchorType = "",
        OffsetX = "0",
        OffsetY = "0",
        Action = "click",
        InputValue = "",
        DelayMs = "300",
        Remark = ""
    };

    private static void ApplyItemToRow(CaptureTargetItem item, CaptureTargetItemRowViewModel row)
    {
        row.TargetID = item.TargetID;
        row.TargetType = item.TargetType ?? "text";
        row.TargetText = item.TargetText ?? "";
        row.AnchorType = string.IsNullOrWhiteSpace(item.AnchorType) ? "" : item.AnchorType;
        row.AnchorText = item.AnchorText ?? "";
        row.OffsetX = item.OffsetX.ToString();
        row.OffsetY = item.OffsetY.ToString();
        row.Action = string.IsNullOrWhiteSpace(item.Action) ? "click" : item.Action;
        row.InputValue = item.InputValue ?? "";
        row.DelayMs = item.DelayMs.HasValue ? item.DelayMs.Value.ToString() : "300";
        row.Remark = item.Remark ?? "";
    }

    [RelayCommand]
    private void AddNavigationTarget()
    {
        NavigationTargets.Add(CreateEmptyRow(NavigationTargets.Count + 1));
        NormalizeIds(NavigationTargets);
    }

    [RelayCommand]
    private void InsertNavigationBelow(CaptureTargetItemRowViewModel? afterRow)
    {
        if (afterRow == null)
            return;
        var index = NavigationTargets.IndexOf(afterRow);
        if (index < 0)
            return;
        NavigationTargets.Insert(index + 1, CreateEmptyRow(0));
        NormalizeIds(NavigationTargets);
    }

    [RelayCommand]
    private void DeleteNavigationTarget(CaptureTargetItemRowViewModel? item)
    {
        if (item == null)
            return;
        NavigationTargets.Remove(item);
        if (NavigationTargets.Count == 0)
            NavigationTargets.Add(CreateEmptyRow(1));
        NormalizeIds(NavigationTargets);
    }

    [RelayCommand]
    private void AddSignTarget()
    {
        SignTargets.Add(CreateEmptyRow(SignTargets.Count + 1));
        NormalizeIds(SignTargets);
    }

    [RelayCommand]
    private void InsertSignBelow(CaptureTargetItemRowViewModel? afterRow)
    {
        if (afterRow == null)
            return;
        var index = SignTargets.IndexOf(afterRow);
        if (index < 0)
            return;
        SignTargets.Insert(index + 1, CreateEmptyRow(0));
        NormalizeIds(SignTargets);
    }

    [RelayCommand]
    private void DeleteSignTarget(CaptureTargetItemRowViewModel? item)
    {
        if (item == null)
            return;
        SignTargets.Remove(item);
        if (SignTargets.Count == 0)
            SignTargets.Add(CreateEmptyRow(1));
        NormalizeIds(SignTargets);
    }

    [RelayCommand]
    private void Confirm()
    {
        try
        {
            if (!int.TryParse(PollIntervalMinutesText.Trim(), out var poll) || poll < 1)
            {
                StatusMessage = "轮询间隔须为 ≥1 的整数（分钟）。";
                return;
            }

            if (!decimal.TryParse(MaxRefundRatePercentText.Trim(), out var maxRefund) || maxRefund < 0 || maxRefund > 100)
            {
                StatusMessage = "最高可接受退款率须在 0～100 之间。";
                return;
            }

            var nav = RowsToItems(NavigationTargets);
            var sign = RowsToItems(SignTargets);
            if (nav.Count == 0)
            {
                StatusMessage = "请至少配置一条导航步骤。";
                return;
            }

            if (sign.Count == 0)
            {
                StatusMessage = "请至少配置一条签约步骤。";
                return;
            }

            CaptureTargetItem? next = null;
            if (!string.IsNullOrWhiteSpace(NextPageRow.TargetText) || !string.IsNullOrWhiteSpace(NextPageRow.AnchorText))
            {
                var one = RowToItem(NextPageRow);
                if (one != null)
                    next = one;
            }

            var data = new AutoAcceptOrderSettingsData
            {
                PollIntervalMinutes = poll,
                MaxRefundRatePercent = maxRefund,
                NavigationSteps = nav,
                SignOrderSteps = sign,
                NextPageStep = next
            };

            _service.Save(data);
            StatusMessage = "保存成功。";
            _closeAction?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    private static void NormalizeIds(ObservableCollection<CaptureTargetItemRowViewModel> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            rows[i].TargetID = i + 1;
    }

    private List<CaptureTargetItem> RowsToItems(ObservableCollection<CaptureTargetItemRowViewModel> rows)
    {
        var list = new List<CaptureTargetItem>();
        foreach (var row in rows)
        {
            var item = RowToItem(row);
            if (item != null)
                list.Add(item);
        }

        return list;
    }

    private CaptureTargetItem? RowToItem(CaptureTargetItemRowViewModel row)
    {
        var type = NormalizeType(row.TargetType);
        if (!string.Equals(type, "window", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(row.TargetText) &&
            string.IsNullOrWhiteSpace(row.AnchorText))
            return null;

        _ = int.TryParse(row.OffsetX, out var ox);
        _ = int.TryParse(row.OffsetY, out var oy);
        var delayMs = 300;
        if (int.TryParse(row.DelayMs?.Trim(), out var d) && d >= 0)
            delayMs = d;
        return new CaptureTargetItem
        {
            TargetID = row.TargetID,
            TargetType = type,
            TargetText = row.TargetText.Trim(),
            AnchorType = string.IsNullOrWhiteSpace(row.AnchorType) ? null : NormalizeType(row.AnchorType),
            AnchorText = string.IsNullOrWhiteSpace(row.AnchorText) ? null : row.AnchorText.Trim(),
            OffsetX = ox,
            OffsetY = oy,
            Action = NormalizeAction(row.Action),
            InputValue = string.IsNullOrWhiteSpace(row.InputValue) ? null : row.InputValue.Trim(),
            DelayMs = delayMs,
            Remark = string.IsNullOrWhiteSpace(row.Remark) ? null : row.Remark.Trim()
        };
    }

    private static string NormalizeType(string? value)
    {
        var v = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(v))
            return "text";
        if (string.Equals(v, "text", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "文字", StringComparison.Ordinal))
            return "text";
        if (string.Equals(v, "inputBox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "输入框", StringComparison.Ordinal))
            return "inputBox";
        if (string.Equals(v, "button", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "按钮", StringComparison.Ordinal))
            return "button";
        if (string.Equals(v, "radioBtn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "单选框", StringComparison.Ordinal))
            return "radioBtn";
        if (string.Equals(v, "dropList", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "下拉框", StringComparison.Ordinal) ||
            string.Equals(v, "下拉列表", StringComparison.Ordinal))
            return "dropList";
        if (string.Equals(v, "checkbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "勾选框", StringComparison.Ordinal))
            return "checkbox";
        if (string.Equals(v, "window", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "窗口", StringComparison.Ordinal))
            return "window";
        if (string.Equals(v, "dialog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "对话框", StringComparison.Ordinal))
            return "dialog";
        return "text";
    }

    private static string NormalizeAction(string? value)
    {
        if (string.Equals(value, "moveTo & click", StringComparison.Ordinal) ||
            string.Equals(value, "moveTo_click", StringComparison.Ordinal))
            return "moveTo_click";
        if (string.Equals(value, "moveTo & click & input", StringComparison.Ordinal) ||
            string.Equals(value, "moveTo_click_input", StringComparison.Ordinal) ||
            string.Equals(value, "moveTo_click_inp", StringComparison.Ordinal))
            return "moveTo_click_input";
        if (string.Equals(value, "click_select", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "click_slecect", StringComparison.OrdinalIgnoreCase))
            return "click_select";
        if (string.Equals(value, "solve_math", StringComparison.OrdinalIgnoreCase))
            return "solve_math";
        if (string.Equals(value, "select", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "选中", StringComparison.Ordinal))
            return "select";
        if (string.Equals(value, "unselect", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "取消选中", StringComparison.Ordinal))
            return "unselect";
        return "click";
    }
}
