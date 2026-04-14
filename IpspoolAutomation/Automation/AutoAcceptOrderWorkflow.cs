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
        CancellationToken ct,
        string? paymentPassword = null)
    {
        if (settings.NavigationSteps.Count == 0)
        {
            progress.Report("自动接单：导航步骤为空，请在 autoAcceptOrder.json 中配置 NavigationSteps。");
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

        var navOk = await _wf.ExecuteCaptureStepsAsync(merchantRoot, settings.NavigationSteps, progress, ct, paymentPassword).ConfigureAwait(false);
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
                await ClickNextPageAsync(merchantRoot, settings.NextPageStep, progress, ct, paymentPassword).ConfigureAwait(false);
        }

        if (allOrderData.Count == 0)
        {
            progress.Report("订单市场无符合条件的订单（或解析失败），跳过签约。");
            _automation.MinimizeWindow(merchantRoot);
            return null;
        }

        var best = allOrderData.OrderByDescending(e => e.UnitPrice).First();
        progress.Report($"已选单价最高订单：{best.OrderId}，单价={best.UnitPrice:F4}，页={best.PageIndex + 1}，行序={best.ItemIndex}。");

        if (!await GoToBestOrderPageAsync(merchantRoot, best.PageIndex, settings.PreviousPageStep, progress, ct, paymentPassword).ConfigureAwait(false))
        {
            progress.Report("跳转到目标订单页失败，结束本次签约。");
            _automation.MinimizeWindow(merchantRoot);
            return null;
        }
        if (!await SignBestOrderByContextMenuAsync(merchantRoot, best, settings.OrderMarketSignMenuItemText, progress, ct).ConfigureAwait(false))
        {
            progress.Report("签约执行失败。");
            _automation.MinimizeWindow(merchantRoot);
            return null;
        }

        if (settings.SignOrderSteps.Count > 0)
        {
            progress.Report("等待签约结果弹窗…");
            await Task.Delay(600, ct).ConfigureAwait(false);
            var postSteps = CloneStepsWithPlaceholders(settings.SignOrderSteps, best.PageIndex, best.ItemIndex);
            var postOk = await _wf.ExecuteCaptureStepsAsync(merchantRoot, postSteps, progress, ct, paymentPassword).ConfigureAwait(false);
            if (!postOk)
                progress.Report("签约后附加步骤执行失败（可检查 SignOrderSteps）。");
        }

        await Task.Delay(300, ct).ConfigureAwait(false);

        var row = new AcceptedOrderRow();
        FillAcceptedMetrics(row, candidate.Username, best, allOrderData);

        var minimized = _automation.MinimizeWindow(merchantRoot);
        progress.Report(minimized ? "已最小化商家窗口。" : "最小化商家窗口失败（可忽略）。");
        await TryCloseSignSuccessDialogAfterMinimizeAsync(merchantRoot, progress, ct).ConfigureAwait(false);

        return row;
    }

    private async Task ClickNextPageAsync(
        AutomationElement merchantRoot,
        CaptureTargetItem? nextPageStep,
        IProgress<string> progress,
        CancellationToken ct,
        string? paymentPassword)
    {
        if (nextPageStep != null)
        {
            var ok = await _wf.ExecuteCaptureStepsAsync(merchantRoot, new List<CaptureTargetItem> { nextPageStep }, progress, ct, paymentPassword).ConfigureAwait(false);
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

    /// <summary>
    /// 扫描结束时默认停在第 9 页，签约前回到 best 所在页（best 为 0-based）。
    /// 优先用「跳转到页面」直达，失败后退化为按「上一页」回退。
    /// </summary>
    private async Task<bool> GoToBestOrderPageAsync(
        AutomationElement merchantRoot,
        int bestPageIndexZeroBased,
        CaptureTargetItem? previousPageStep,
        IProgress<string> progress,
        CancellationToken ct,
        string? paymentPassword)
    {
        const int lastPageIndexZeroBased = 8;
        if (bestPageIndexZeroBased < 0 || bestPageIndexZeroBased > lastPageIndexZeroBased)
            return false;

        if (bestPageIndexZeroBased == lastPageIndexZeroBased)
            return true;

        var targetPage = bestPageIndexZeroBased + 1;
        progress.Report($"准备回到目标页：第 {targetPage} 页。");
        if (_automation.TryJumpOrderMarketToPage(merchantRoot, targetPage))
        {
            progress.Report($"已通过跳转框回到第 {targetPage} 页。");
            await Task.Delay(450, ct).ConfigureAwait(false);
            return true;
        }

        var needBack = lastPageIndexZeroBased - bestPageIndexZeroBased;
        progress.Report($"跳转框不可用，改为点击上一页 {needBack} 次。");
        for (var i = 0; i < needBack; i++)
        {
            ct.ThrowIfCancellationRequested();
            await ClickPreviousPageAsync(merchantRoot, previousPageStep, progress, ct, paymentPassword).ConfigureAwait(false);
        }

        return true;
    }

    private async Task ClickPreviousPageAsync(
        AutomationElement merchantRoot,
        CaptureTargetItem? previousPageStep,
        IProgress<string> progress,
        CancellationToken ct,
        string? paymentPassword)
    {
        if (previousPageStep != null)
        {
            var ok = await _wf.ExecuteCaptureStepsAsync(merchantRoot, new List<CaptureTargetItem> { previousPageStep }, progress, ct, paymentPassword).ConfigureAwait(false);
            if (!ok)
                progress.Report("上一页步骤可能未成功，仍将尝试继续。");
            await Task.Delay(400, ct).ConfigureAwait(false);
            return;
        }

        var btn = _automation.FindDescendantByNameContains(merchantRoot, ControlType.Button, "上一页");
        if (btn == null)
        {
            progress.Report("未找到「上一页」按钮。");
            await Task.Delay(300, ct).ConfigureAwait(false);
            return;
        }

        _automation.InvokeButton(btn);
        await Task.Delay(500, ct).ConfigureAwait(false);
    }

    private async Task<bool> SignBestOrderByContextMenuAsync(
        AutomationElement merchantRoot,
        OrderMarketEntry best,
        string? configuredMenuText,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var grid = HelperGridReader.FindMainGrid(merchantRoot);
        if (grid == null)
        {
            progress.Report("签约前校验：未找到订单市场表格。");
            return false;
        }

        var expectedOrderId = (best.OrderId ?? "").Trim();
        var row = MerchantOrderMarketReader.GetDataItemByOneBasedIndex(merchantRoot, best.ItemIndex);
        if (row == null)
        {
            progress.Report($"签约前校验：未找到当前页第 {best.ItemIndex} 行。");
            return false;
        }

        if (!MerchantOrderMarketReader.TryReadOrderIdForRow(grid, row, out var currentOrderId))
        {
            progress.Report($"签约前校验：当前页第 {best.ItemIndex} 行订单号读取失败。");
            return false;
        }

        if (!string.Equals(currentOrderId, expectedOrderId, StringComparison.Ordinal))
        {
            progress.Report($"签约前校验不一致：当前页第 {best.ItemIndex} 行订单号={currentOrderId}，预期={expectedOrderId}；尝试按订单号再次定位。");
            row = MerchantOrderMarketReader.FindDataItemByOrderId(merchantRoot, expectedOrderId);
            if (row == null)
            {
                progress.Report($"签约前校验失败：当前页未找到订单号={expectedOrderId}。");
                return false;
            }
            if (!MerchantOrderMarketReader.TryReadOrderIdForRow(grid, row, out currentOrderId) ||
                !string.Equals(currentOrderId, expectedOrderId, StringComparison.Ordinal))
            {
                progress.Report($"签约前校验失败：按订单号定位后再次读取不一致，当前={currentOrderId}，预期={expectedOrderId}。");
                return false;
            }
        }

        progress.Report($"签约前校验通过：订单号={currentOrderId}，准备右键签约。");
        _automation.TryEnsureGridRowReadyForContextMenu(row);
        _automation.LeftClickGridRowForContextMenu(row);//选择行
        await Task.Delay(500, ct).ConfigureAwait(false);
        _automation.RightClickGridRowForContextMenu(row);//右键签约
        await Task.Delay(500, ct).ConfigureAwait(false);

        var menuText = string.IsNullOrWhiteSpace(configuredMenuText)
            ? "签约该订单(订单市场)"
            : configuredMenuText.Trim();
        var menuItem = _automation.FindMenuItem(menuText) ?? _automation.FindMenuItem("签约该订单(订单市场)");
        if (menuItem == null)
        {
            progress.Report($"未找到右键菜单项：{menuText}");
            return false;
        }

        _automation.InvokeButton(menuItem);
        progress.Report($"已点击右键菜单项：{menuText}");
        await Task.Delay(300, ct).ConfigureAwait(false);
        return true;
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

    private async Task TryCloseSignSuccessDialogAfterMinimizeAsync(
        AutomationElement merchantRoot,
        IProgress<string> progress,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(attempt == 0 ? 400 : 450, ct).ConfigureAwait(false);
            var dialog = _automation.FindDialogWindow(merchantRoot, "迅捷云商家版");
            if (dialog == null)
            {
                if (attempt == 2)
                    progress.Report("最小化后未找到「签约成功」弹窗（已重试 3 次）。");
                continue;
            }

            _automation.TryRestoreWindowNormal(dialog);
            await Task.Delay(200, ct).ConfigureAwait(false);

            var confirm = FindSignSuccessConfirmButton(dialog);
            if (confirm == null)
            {
                progress.Report("最小化后找到「签约成功」弹窗，但未找到可点的「确定」类按钮。");
                return;
            }

            if (TryInvokeOrLeftClickButton(confirm))
            {
                progress.Report("最小化后已补偿关闭「签约成功」弹窗。");
                await Task.Delay(150, ct).ConfigureAwait(false);
                return;
            }

            progress.Report($"最小化后点击「确定」可能未生效（第 {attempt + 1}/3 次），将重试。");
        }
    }

    private AutomationElement? FindSignSuccessConfirmButton(AutomationElement dialog)
    {
        var b = _automation.FindDescendantByNameContains(dialog, ControlType.Button, "确定");
        if (b != null)
            return b;

        try
        {
            var all = _automation.FindAll(dialog, ControlType.Button, TreeScope.Descendants);
            foreach (AutomationElement el in all)
            {
                try
                {
                    var n = el.Current.Name ?? "";
                    if (n.Contains("确定", StringComparison.Ordinal))
                        return el;
                    if (n.Equals("OK", StringComparison.OrdinalIgnoreCase))
                        return el;
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private bool TryInvokeOrLeftClickButton(AutomationElement button)
    {
        try
        {
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var ipObj) && ipObj is InvokePattern ip)
            {
                ip.Invoke();
                return true;
            }
        }
        catch
        {
            // fall through to mouse click
        }

        try
        {
            _automation.SetFocus(button);
        }
        catch
        {
            // ignore
        }

        try
        {
            _automation.LeftClickElement(button);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
