using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Models.Capture;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.ViewModels;

public sealed partial class CaptureSettingsViewModel : ObservableObject
{
    private readonly ICaptureTargetListPersistence _persistence;
    private Action? _closeAction;

    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<CaptureTargetItemRowViewModel> CaptureTargets { get; } = new();
    public IReadOnlyList<string> TargetTypeOptions { get; } = new[] { "text", "inputBox", "button", "radioBtn", "dropList", "window", "dialog" };
    public IReadOnlyList<string> ActionOptions { get; } = new[] { "click", "moveTo_click", "moveTo_click_input", "click_select", "solve_math" };

    public CaptureSettingsViewModel(
        ICaptureTargetListPersistence persistence,
        IEnumerable<CaptureTargetItem>? initialItems = null)
    {
        _persistence = persistence;
        var source = initialItems?.ToList() ?? persistence.Load().CaptureTargetList;
        if (source.Count == 0)
        {
            AddTarget();
            return;
        }

        foreach (var item in source.OrderBy(x => x.TargetID))
        {
            CaptureTargets.Add(new CaptureTargetItemRowViewModel
            {
                TargetID = item.TargetID,
                TargetType = NormalizeType(item.TargetType),
                TargetText = item.TargetText ?? "",
                AnchorType = string.IsNullOrWhiteSpace(item.AnchorType) ? "" : NormalizeType(item.AnchorType),
                AnchorText = item.AnchorText ?? "",
                OffsetX = item.OffsetX.ToString(),
                OffsetY = item.OffsetY.ToString(),
                Action = NormalizeAction(item.Action),
                InputValue = item.InputValue ?? "",
                DelayMs = item.DelayMs.HasValue ? item.DelayMs.Value.ToString() : "300",
                Remark = item.Remark ?? ""
            });
        }
        NormalizeIds();
    }

    public void SetCloseAction(Action action) => _closeAction = action;

    [RelayCommand]
    private void AddTarget()
    {
        CaptureTargets.Add(new CaptureTargetItemRowViewModel
        {
            TargetID = CaptureTargets.Count + 1,
            TargetType = "text",
            AnchorType = "",
            OffsetX = "0",
            OffsetY = "0",
            Action = "click",
            InputValue = "",
            DelayMs = "300",
            Remark = ""
        });
        NormalizeIds();
    }

    [RelayCommand]
    private void InsertTargetBelow(CaptureTargetItemRowViewModel? afterRow)
    {
        if (afterRow == null)
            return;
        var index = CaptureTargets.IndexOf(afterRow);
        if (index < 0)
            return;
        CaptureTargets.Insert(index + 1, new CaptureTargetItemRowViewModel
        {
            TargetType = "text",
            AnchorType = "",
            OffsetX = "0",
            OffsetY = "0",
            Action = "click",
            InputValue = "",
            DelayMs = "300",
            Remark = ""
        });
        NormalizeIds();
    }

    [RelayCommand]
    private void DeleteTarget(CaptureTargetItemRowViewModel? item)
    {
        if (item == null)
            return;
        CaptureTargets.Remove(item);
        NormalizeIds();
    }

    [RelayCommand]
    private void Confirm()
    {
        try
        {
            var list = new List<CaptureTargetItem>();
            foreach (var row in CaptureTargets)
            {
                var type = NormalizeType(row.TargetType);
                if (!string.Equals(type, "window", StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(row.TargetText) &&
                    string.IsNullOrWhiteSpace(row.AnchorText))
                    continue;

                _ = int.TryParse(row.OffsetX, out var ox);
                _ = int.TryParse(row.OffsetY, out var oy);
                var delayMs = 300;
                if (int.TryParse(row.DelayMs?.Trim(), out var d) && d >= 0)
                    delayMs = d;
                list.Add(new CaptureTargetItem
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
                });
            }

            _persistence.Save(new CaptureTargetSettings { CaptureTargetList = list });
            StatusMessage = $"保存成功，共 {list.Count} 条目标。";
            _closeAction?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    private void NormalizeIds()
    {
        for (var i = 0; i < CaptureTargets.Count; i++)
            CaptureTargets[i].TargetID = i + 1;
    }

    private string NormalizeType(string? value)
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
        if (string.Equals(v, "window", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "窗口", StringComparison.Ordinal))
            return "window";
        if (string.Equals(v, "dialog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "对话框", StringComparison.Ordinal))
            return "dialog";
        return "text";
    }

    private string NormalizeAction(string? value)
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
        return "click";
    }
}

public sealed partial class CaptureTargetItemRowViewModel : ObservableObject
{
    [ObservableProperty] private int _targetID;
    [ObservableProperty] private string _targetType = "text";
    [ObservableProperty] private string _targetText = "";
    [ObservableProperty] private string _anchorType = "";
    [ObservableProperty] private string _anchorText = "";
    [ObservableProperty] private string _offsetX = "0";
    [ObservableProperty] private string _offsetY = "0";
    [ObservableProperty] private string _action = "click";
    [ObservableProperty] private string _inputValue = "";
    [ObservableProperty] private string _delayMs = "300";
    [ObservableProperty] private string _remark = "";
}

