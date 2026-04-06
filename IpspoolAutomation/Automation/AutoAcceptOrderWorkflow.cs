using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using IpspoolAutomation.Models;
using IpspoolAutomation.Models.Capture;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.Automation;

/// <summary>自动接单：辅助列表 → 显示此号 → 商家导航 → 订单市场 9 页采集 → 选单签约 → 最小化。</summary>
public sealed class AutoAcceptOrderWorkflow
{
    private readonly IAutomationService _automation;
    private readonly string _helperPath;
    private readonly string _merchantPath;
    private readonly XunjieAutomationWorkflow _wf;

    public AutoAcceptOrderWorkflow(IAutomationService automation, string helperPath, string merchantPath)
    {
        _automation = automation;
        _helperPath = helperPath;
        _merchantPath = merchantPath;
        _wf = new XunjieAutomationWorkflow(automation, helperPath, merchantPath);
    }

    public async Task<AcceptedOrderRow?> ProcessCandidateAsync(
        AutoAcceptHelperCandidate candidate,
        AutoAcceptOrderSettingsData settings,
        decimal targetRefundRatePercent,
        IProgress<string> progress,
        CancellationToken ct)
    {
        if (settings.NavigationSteps.Count == 0)
        {
            progress.Report("自动接单：导航步骤为空，请在 autoAcceptOrder.json 中配置 NavigationSteps。");
            return null;
        }

        if (settings.SignOrderSteps.Count == 0)
        {
            progress.Report("自动接单：签约步骤为空，请配置 SignOrderSteps。");
            return null;
        }

        var withdrawRow = new WithdrawCandidateRow(candidate.Username, candidate.Score, candidate.RowId);
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法启动或附着迅捷辅助窗口。");
            return null;
        }

        await Task.Delay(300, ct).ConfigureAwait(false);

        var merchantPidsBefore = _automation.EnumerateMainWindowProcessIds(_merchantPath);
        if (!await _wf.ShowHelperAccountForAutomationAsync(helperRoot, withdrawRow, progress, ct).ConfigureAwait(false))
            return null;

        progress.Report("正在附着商家版窗口…");
        var merchantRoot = _automation.AttachMerchantAfterShowAccount(_merchantPath, merchantPidsBefore);
        if (merchantRoot == null)
        {
            progress.Report("未能附着商家版窗口。");
            return null;
        }

        EnsureWindowNormal(merchantRoot);

        var navOk = await _wf.ExecuteCaptureStepsAsync(merchantRoot, settings.NavigationSteps, progress, ct).ConfigureAwait(false);
        if (!navOk)
        {
            progress.Report("自动接单：导航步骤执行失败。");
            _automation.MinimizeWindow(merchantRoot);
            return null;
        }

        await Task.Delay(400, ct).ConfigureAwait(false);

        var allOrderData = new List<OrderMarketEntry>();
        for (var pageIndex = 0; pageIndex < 9; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report($"正在读取订单市场第 {pageIndex + 1}/9 页…");
            var pageRows = MerchantOrderMarketReader.ReadFilteredPage(merchantRoot, pageIndex, targetRefundRatePercent, progress);
            allOrderData.AddRange(pageRows);

            if (pageIndex < 8)
                await ClickNextPageAsync(merchantRoot, settings.NextPageStep, progress, ct).ConfigureAwait(false);
        }

        if (allOrderData.Count == 0)
        {
            progress.Report("订单市场无符合条件的订单（或解析失败），跳过签约。");
            _automation.MinimizeWindow(merchantRoot);
            return null;
        }

        var best = allOrderData.OrderByDescending(e => e.UnitPrice).First();
        progress.Report($"已选单价最高订单：{best.OrderId}，单价={best.UnitPrice:F4}，页={best.PageIndex + 1}，行序={best.ItemIndex}。");

        var signSteps = CloneStepsWithPlaceholders(settings.SignOrderSteps, best.PageIndex, best.ItemIndex);
        var signOk = await _wf.ExecuteCaptureStepsAsync(merchantRoot, signSteps, progress, ct).ConfigureAwait(false);
        if (!signOk)
        {
            progress.Report("签约步骤执行失败。");
            _automation.MinimizeWindow(merchantRoot);
            return null;
        }

        await Task.Delay(300, ct).ConfigureAwait(false);

        var row = new AcceptedOrderRow();
        FillAcceptedMetrics(row, candidate.Username, best, allOrderData);

        var minimized = _automation.MinimizeWindow(merchantRoot);
        progress.Report(minimized ? "已最小化商家窗口。" : "最小化商家窗口失败（可忽略）。");

        return row;
    }

    private async Task ClickNextPageAsync(
        AutomationElement merchantRoot,
        CaptureTargetItem? nextPageStep,
        IProgress<string> progress,
        CancellationToken ct)
    {
        if (nextPageStep != null)
        {
            var ok = await _wf.ExecuteCaptureStepsAsync(merchantRoot, new List<CaptureTargetItem> { nextPageStep }, progress, ct).ConfigureAwait(false);
            if (!ok)
                progress.Report("翻页步骤可能未成功，仍将尝试继续。");
            await Task.Delay(400, ct).ConfigureAwait(false);
            return;
        }

        var btn = _automation.FindDescendantByNameContains(merchantRoot, ControlType.Button, "下一页");
        if (btn == null)
        {
            progress.Report("未找到「下一页」按钮，后续页数据可能重复或为空。");
            await Task.Delay(300, ct).ConfigureAwait(false);
            return;
        }

        _automation.InvokeButton(btn);
        await Task.Delay(500, ct).ConfigureAwait(false);
    }

    private static void FillAcceptedMetrics(AcceptedOrderRow row, string username, OrderMarketEntry best, IReadOnlyList<OrderMarketEntry> all)
    {
        row.Username = username;
        row.OrderNumber = best.OrderId;
        var min = all.Min(x => x.UnitPrice);
        var sum = all.Sum(x => x.UnitPrice);
        var n = all.Count;
        var avg = n > 0 ? sum / n : 0m;
        row.UnitPrice = best.UnitPrice;
        row.HighDiff = best.UnitPrice - min;
        row.HighDiffRatioPercent = min > 0 ? Math.Round(row.HighDiff / min * 100m, 2) : 0m;
        row.AvgDiff = best.UnitPrice - avg;
        row.AvgDiffRatioPercent = avg > 0 ? Math.Round(row.AvgDiff / avg * 100m, 2) : 0m;
    }

    private static List<CaptureTargetItem> CloneStepsWithPlaceholders(IReadOnlyList<CaptureTargetItem> steps, int pageIndexZeroBased, int itemIndex)
    {
        var json = JsonSerializer.Serialize(steps);
        var list = JsonSerializer.Deserialize<List<CaptureTargetItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<CaptureTargetItem>();
        var p1 = (pageIndexZeroBased + 1).ToString(CultureInfo.InvariantCulture);
        var i1 = itemIndex.ToString(CultureInfo.InvariantCulture);
        foreach (var c in list)
        {
            c.TargetText = ReplacePh(c.TargetText, p1, i1) ?? "";
            c.InputValue = ReplacePh(c.InputValue, p1, i1);
            c.AnchorText = ReplacePh(c.AnchorText, p1, i1);
        }

        return list.OrderBy(x => x.TargetID).ToList();
    }

    private static string? ReplacePh(string? s, string pageOneBased, string itemStr)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Replace("{pageIndex}", pageOneBased, StringComparison.Ordinal)
            .Replace("{itemIndex}", itemStr, StringComparison.Ordinal);
    }

    private static void EnsureWindowNormal(AutomationElement window)
    {
        try
        {
            if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var wpObj) && wpObj is WindowPattern wp)
                wp.SetWindowVisualState(WindowVisualState.Normal);
        }
        catch
        {
            // ignore
        }
    }
}
