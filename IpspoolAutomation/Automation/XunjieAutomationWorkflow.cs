using System.Diagnostics;
using System.Windows.Automation;
using IpspoolAutomation.Models;
using IpspoolAutomation.Models.Capture;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.Automation;

public class XunjieAutomationWorkflow
{
    /// <summary>手续费比例：手续费 = 兑换积分 × WithdrawFeeRatio（与 ComputeWithdrawAmountCoins 内一致）。</summary>
    public const double WithdrawFeeRatio = 0.05;
    private static readonly int[] WithdrawOptions = { 1000, 500, 300, 200, 100 };
    private const int ScoreToCoinRatio = 1000;

    private readonly IAutomationService _automation;
    private readonly string _helperPath;
    private readonly string _merchantPath;
    private const int MinDailyCheckScore = 105000;

    public XunjieAutomationWorkflow(IAutomationService automation, string helperPath, string merchantPath)
    {
        _automation = automation;
        _helperPath = helperPath;
        _merchantPath = merchantPath;
    }

    /// <summary>
    /// 按需求文档：在可提现积分 score 下，从大到小尝试讯币档位，使剩余积分 &gt; 0 的第一档为可提现讯币数量。
    /// </summary>
    public static int ComputeWithdrawAmountCoins(int score)
    {
        foreach (var option in WithdrawOptions)
        {
            var coinCostPoints = option * ScoreToCoinRatio;
            var feePoints = (int)Math.Round(option * ScoreToCoinRatio * WithdrawFeeRatio, MidpointRounding.AwayFromZero);
            var leftScore = score - coinCostPoints - feePoints;
            if (leftScore > 0)
                return option;
        }
        return 0;
    }

    /// <summary>
    /// 兑换页固定「兑换额度」Q（讯币）时，辅助列表筛选阈值：可提收益需 ≥ Q×1000 + Q×1000×5% +（勾选保留235000 时 +235000）。
    /// </summary>
    public static int ComputeExchangeMinScoreForQuota(int optionCoins, bool keepReserve235000)
    {
        var coinCostPoints = optionCoins * ScoreToCoinRatio;
        var feePoints = (int)Math.Round(coinCostPoints * WithdrawFeeRatio, MidpointRounding.AwayFromZero);
        var reserve = keepReserve235000 ? 235000 : 0;
        return coinCostPoints + feePoints + reserve;
    }

    public async Task RunAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        progress.Report("正在启动迅捷小辅助...");
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法找到或启动迅捷小辅助窗口");
            return;
        }
        progress.Report("迅捷小辅助已就绪");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        progress.Report("正在启动迅捷云商家版...");
        var merchantRoot = _automation.LaunchOrAttach(_merchantPath);
        if (merchantRoot == null)
        {
            progress.Report("无法找到或启动迅捷云商家版窗口");
            return;
        }
        progress.Report("迅捷云商家版已就绪");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        progress.Report("自动化流程占位完成，请在 Workflow 中补充具体业务步骤。");
    }

    /// <summary>
    /// 执行 Notion《自动化操作步骤》：筛选可提收益 &gt; 105000 的账号，依次「显示此号」后在商家端讯币兑现与会员中心提现。
    /// </summary>
    public async Task<WithdrawRunResult?> RunWithdrawAsync(
        IProgress<string> progress,
        string recipientPhone,
        string withdrawName,
        string alipayAccount,
        int minScoreExclusive,
        IReadOnlyList<CaptureTargetItem>? captureTargets = null,
        int? fixedWithdrawCoins = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
        {
            progress.Report("未设置收款人手机号：请在「设置」中填写「收款人手机号」并保存。");
            return null;
        }

        progress.Report("正在启动/附着迅捷小辅助...");
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法找到或启动迅捷小辅助窗口。");
            return null;
        }
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report("正在读取辅助软件账号列表...");
        var raw = HelperGridReader.CollectCandidates(helperRoot, progress, minScoreExclusive);
        var candidates = DeduplicateByUsername(raw);
        progress.Report($"符合条件的账号数（可提收益>{minScoreExclusive}）：{candidates.Count}");
        if (candidates.Count == 0)
        {
            progress.Report("没有需要处理的账号，结束。");
            return null;
        }

        var sw = Stopwatch.StartNew();
        var processedCount = 0;
        long totalCoins = 0;
        var detailRows = new List<WithdrawDetailItem>();

        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coins = fixedWithdrawCoins ?? ComputeWithdrawAmountCoins(c.Score);
            progress.Report(fixedWithdrawCoins.HasValue
                ? $"处理用户 rowID={c.RowId}，用户名={c.Username}，可提收益(积分)={c.Score}，提现讯币(固定档位)={coins}。"
                : $"处理用户 rowID={c.RowId}，用户名={c.Username}，可提收益(积分)={c.Score}，计算提现讯币={coins}。");
            if (coins <= 0)
            {
                progress.Report($"提现讯币为 0，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                continue;
            }

            helperRoot = _automation.LaunchOrAttach(_helperPath) ?? helperRoot;

            var merchantPidsBeforeShow = _automation.EnumerateMainWindowProcessIds(_merchantPath);
            AutomationDevLog.Report(progress, $"「显示此号」前已存在的商家版进程数：{merchantPidsBeforeShow.Count}。");

            if (!await ShowAccountOnMerchantAsync(helperRoot, c, progress, cancellationToken).ConfigureAwait(false))
                continue;

            AutomationDevLog.Report(progress, "正在附着当前账号对应的迅捷云商家版窗口（多实例时优先新进程，其次前台窗口）…");
            var merchantRoot = _automation.AttachMerchantAfterShowAccount(_merchantPath, merchantPidsBeforeShow);
            if (merchantRoot == null)
            {
                progress.Report("未能附着商家版窗口，跳过该用户。");
                continue;
            }
            EnsureWindowNormal(merchantRoot);

            var configuredDone = false;
            if (captureTargets != null && captureTargets.Count > 0)
            {
                configuredDone = await ExecuteConfiguredMerchantActionsAsync(
                    merchantRoot, captureTargets, coins, recipientPhone, withdrawName, alipayAccount, progress, cancellationToken).ConfigureAwait(false);
            }
            if (!configuredDone)
            {
                if (!await MerchantFillExchangeAsync(merchantRoot, coins, progress, cancellationToken).ConfigureAwait(false))
                {
                    progress.Report($"讯币兑现步骤未完成，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                    continue;
                }
                await Task.Delay(600, cancellationToken).ConfigureAwait(false);

                if (!await MerchantFillMemberCenterAsync(merchantRoot, recipientPhone, coins, progress, cancellationToken).ConfigureAwait(false))
                {
                    progress.Report($"会员中心提现步骤未完成，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                    continue;
                }
            }
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);

            var minimized = _automation.MinimizeWindow(merchantRoot);
            progress.Report(minimized
                ? $"已最小化商家窗口（rowID={c.RowId}），继续下一账号。"
                : $"尝试最小化商家窗口失败（rowID={c.RowId}），窗口可能被目标程序拦截。");
            detailRows.Add(CreateWithdrawDetailItem(c.Username, c.Score, coins));
            processedCount++;
            totalCoins += coins;
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        var elapsedSeconds = sw.Elapsed.TotalSeconds;
        progress.Report($"自动提现流程全部结束。共处理{processedCount}个账号，共计：{totalCoins}讯币，耗时：{elapsedSeconds:F1}秒");
        return new WithdrawRunResult(processedCount, totalCoins, elapsedSeconds, detailRows);
    }

    /// <summary>
    /// 「仅提现不兑换」：按「讯币」列与提现额度筛选，仅执行 <paramref name="steps"/>（withdraw_only.json），不执行内置讯币兑现与会员中心。
    /// </summary>
    public async Task<WithdrawRunResult?> RunWithdrawOnlyAsync(
        IProgress<string> progress,
        string recipientPhone,
        string withdrawName,
        string alipayAccount,
        int minScoreExclusive,
        bool useAutoWithdrawQuota,
        int fixedWithdrawQuotaCoins,
        IReadOnlyList<CaptureTargetItem> steps,
        int? fixedWithdrawCoinsForExecution,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
        {
            progress.Report("未设置收款人手机号：请在「设置」中填写「收款人手机号」并保存。");
            return null;
        }

        if (steps == null || steps.Count == 0)
        {
            progress.Report("仅提现不兑换配置为空：请先在「设置」中配置「仅兑换不提现」（withdraw_only.json）。");
            return null;
        }

        progress.Report("正在启动/附着迅捷小辅助...");
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法找到或启动迅捷小辅助窗口。");
            return null;
        }
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report("正在读取辅助软件账号列表（仅提现不兑换：讯币列≥提现额度）…");
        var raw = HelperGridReader.CollectCandidatesForWithdrawOnly(
            helperRoot,
            progress,
            minScoreExclusive,
            useAutoWithdrawQuota,
            fixedWithdrawQuotaCoins,
            ComputeWithdrawAmountCoins);
        var candidates = DeduplicateByUsername(raw);
        progress.Report($"符合条件的账号数：{candidates.Count}");
        if (candidates.Count == 0)
        {
            progress.Report("没有需要处理的账号，结束。");
            return null;
        }

        var sw = Stopwatch.StartNew();
        var processedCount = 0;
        long totalCoins = 0;
        var detailRows = new List<WithdrawDetailItem>();

        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coins = fixedWithdrawCoinsForExecution ?? ComputeWithdrawAmountCoins(c.Score);
            progress.Report($"[仅提现不兑换] rowID={c.RowId}，用户名={c.Username}，可提收益(积分)={c.Score}，执行讯币={coins}。");
            if (coins <= 0)
            {
                progress.Report($"执行讯币为 0，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                continue;
            }

            helperRoot = _automation.LaunchOrAttach(_helperPath) ?? helperRoot;

            var merchantPidsBeforeShow = _automation.EnumerateMainWindowProcessIds(_merchantPath);
            AutomationDevLog.Report(progress, $"「显示此号」前已存在的商家版进程数：{merchantPidsBeforeShow.Count}。");

            if (!await ShowAccountOnMerchantAsync(helperRoot, c, progress, cancellationToken).ConfigureAwait(false))
                continue;

            AutomationDevLog.Report(progress, "正在附着当前账号对应的迅捷云商家版窗口（多实例时优先新进程，其次前台窗口）…");
            var merchantRoot = _automation.AttachMerchantAfterShowAccount(_merchantPath, merchantPidsBeforeShow);
            if (merchantRoot == null)
            {
                progress.Report("未能附着商家版窗口，跳过该用户。");
                continue;
            }
            EnsureWindowNormal(merchantRoot);

            var ok = await ExecuteConfiguredMerchantActionsAsync(
                merchantRoot, steps, coins, recipientPhone, withdrawName, alipayAccount, progress, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                progress.Report($"仅提现不兑换脚本未完成，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                _automation.MinimizeWindow(merchantRoot);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);

            var minimized = _automation.MinimizeWindow(merchantRoot);
            progress.Report(minimized
                ? $"已最小化商家窗口（rowID={c.RowId}），继续下一账号。"
                : $"尝试最小化商家窗口失败（rowID={c.RowId}），窗口可能被目标程序拦截。");
            detailRows.Add(CreateWithdrawDetailItem(c.Username, c.Score, coins));
            processedCount++;
            totalCoins += coins;
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        var elapsedSeconds = sw.Elapsed.TotalSeconds;
        progress.Report($"仅提现不兑换流程全部结束。共处理{processedCount}个账号，共计：{totalCoins}讯币，耗时：{elapsedSeconds:F1}秒");
        return new WithdrawRunResult(processedCount, totalCoins, elapsedSeconds, detailRows);
    }

    /// <summary>
    /// 按 <paramref name="steps"/>（仅兑换设置 / exchange_score.json）在符合条件的账号上执行商家端配置步骤；不执行内置讯币兑现与会员中心提现。
    /// </summary>
    public async Task<WithdrawRunResult?> RunExchangeScoreAsync(
        IProgress<string> progress,
        string recipientPhone,
        string withdrawName,
        string alipayAccount,
        int minScoreExclusive,
        IReadOnlyList<CaptureTargetItem> steps,
        bool useAutoExchangeCoins = true,
        int? fixedExchangeCoins = null,
        bool keepReserve235000ForQuota = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
        {
            progress.Report("未设置收款人手机号：请在「设置」中填写「收款人手机号」并保存。");
            return null;
        }

        if (steps == null || steps.Count == 0)
        {
            progress.Report("仅兑换设置为空：请先在「设置」中配置「仅兑换设置」并保存。");
            return null;
        }

        progress.Report("正在启动/附着迅捷小辅助...");
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法找到或启动迅捷小辅助窗口。");
            return null;
        }
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report("正在读取辅助软件账号列表...");
        List<WithdrawCandidateRow> raw;
        int? exchangeMinInclusive = null;
        if (useAutoExchangeCoins)
        {
            raw = HelperGridReader.CollectCandidates(helperRoot, progress, minScoreExclusive);
        }
        else
        {
            if (fixedExchangeCoins is not { } q || q <= 0)
            {
                progress.Report("固定兑换额度无效，请重新选择兑换额度。");
                return null;
            }
            exchangeMinInclusive = ComputeExchangeMinScoreForQuota(q, keepReserve235000ForQuota);
            progress.Report(
                $"兑换额度={q} 讯币；兑换积分阈值（可提收益需≥）={exchangeMinInclusive}（含 5% 手续费积分，{(keepReserve235000ForQuota ? "含保留 235000" : "不含保留 235000")}）。");
            raw = HelperGridReader.CollectCandidatesWithMinScoreInclusive(helperRoot, progress, exchangeMinInclusive.Value);
        }

        var candidates = DeduplicateByUsername(raw);
        if (useAutoExchangeCoins)
            progress.Report($"符合条件的账号数（可提收益>{minScoreExclusive}）：{candidates.Count}");
        else
            progress.Report($"符合条件的账号数（可提收益≥{exchangeMinInclusive}）：{candidates.Count}");

        if (candidates.Count == 0)
        {
            progress.Report("没有需要处理的账号，结束。");
            return null;
        }

        var sw = Stopwatch.StartNew();
        var processedCount = 0;
        long totalCoins = 0;
        var detailRows = new List<WithdrawDetailItem>();

        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coins = useAutoExchangeCoins
                ? ComputeWithdrawAmountCoins(c.Score)
                : fixedExchangeCoins!.Value;
            progress.Report($"[仅兑换] 处理用户 rowID={c.RowId}，用户名={c.Username}，可提收益(积分)={c.Score}，{(useAutoExchangeCoins ? "计算" : "固定")}讯币={coins}。");
            if (coins <= 0)
            {
                progress.Report($"计算讯币为 0，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                continue;
            }

            helperRoot = _automation.LaunchOrAttach(_helperPath) ?? helperRoot;

            var merchantPidsBeforeShow = _automation.EnumerateMainWindowProcessIds(_merchantPath);
            AutomationDevLog.Report(progress, $"「显示此号」前已存在的商家版进程数：{merchantPidsBeforeShow.Count}。");

            if (!await ShowAccountOnMerchantAsync(helperRoot, c, progress, cancellationToken).ConfigureAwait(false))
                continue;

            AutomationDevLog.Report(progress, "正在附着当前账号对应的迅捷云商家版窗口（多实例时优先新进程，其次前台窗口）…");
            var merchantRoot = _automation.AttachMerchantAfterShowAccount(_merchantPath, merchantPidsBeforeShow);
            if (merchantRoot == null)
            {
                progress.Report("未能附着商家版窗口，跳过该用户。");
                continue;
            }
            EnsureWindowNormal(merchantRoot);

            var ok = await ExecuteConfiguredMerchantActionsAsync(
                merchantRoot, steps, coins, recipientPhone, withdrawName, alipayAccount, progress, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                progress.Report($"仅兑换脚本未完成，跳过（rowID={c.RowId}，用户名={c.Username}）。");
                _automation.MinimizeWindow(merchantRoot);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);

            var minimized = _automation.MinimizeWindow(merchantRoot);
            progress.Report(minimized
                ? $"已最小化商家窗口（rowID={c.RowId}），继续下一账号。"
                : $"尝试最小化商家窗口失败（rowID={c.RowId}），窗口可能被目标程序拦截。");
            detailRows.Add(CreateWithdrawDetailItem(c.Username, c.Score, coins));
            processedCount++;
            totalCoins += coins;
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        var elapsedSeconds = sw.Elapsed.TotalSeconds;
        progress.Report($"仅兑换流程全部结束。共处理{processedCount}个账号，共计：{totalCoins}讯币，耗时：{elapsedSeconds:F1}秒");
        return new WithdrawRunResult(processedCount, totalCoins, elapsedSeconds, detailRows);
    }

    /// <summary>兑换积分 = 讯币对应积分；手续费 = 兑换积分 × WithdrawFeeRatio（取整与流程一致）。</summary>
    public static WithdrawDetailItem CreateWithdrawDetailItem(string username, int score, int coins)
    {
        var coinCostPoints = coins * ScoreToCoinRatio;
        var feePoints = (int)Math.Round(coinCostPoints * WithdrawFeeRatio, MidpointRounding.AwayFromZero);
        var remaining = score - coinCostPoints - feePoints;
        return new WithdrawDetailItem
        {
            Account = username,
            Points = coinCostPoints,
            Fee = feePoints,
            RemainingPoints = remaining,
            ExchangedCoins = coins,
            Status = "成功"
        };
    }

    private async Task<bool> ExecuteConfiguredMerchantActionsAsync(
        AutomationElement merchantRoot,
        IReadOnlyList<CaptureTargetItem> captureTargets,
        int withdrawAmountCoins,
        string recipientPhone,
        string withdrawName,
        string alipayAccount,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var steps = captureTargets.OrderBy(x => x.TargetID).ToList();
        if (steps.Count == 0)
            return false;

        AutomationDevLog.Report(progress, $"检测到捕捉设置，共 {steps.Count} 步，按配置执行商家操作。");
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryResolveStepTarget(merchantRoot, step, out var target, out var anchor, out var scopeRoot, out var resolveDebug))
            {
                if (!string.IsNullOrWhiteSpace(resolveDebug))
                    AutomationDevLog.Report(progress, resolveDebug);
                progress.Report($"配置步骤#{step.TargetID} 未找到目标控件（targetType={step.TargetType}, targetText={step.TargetText}）。");
                return false;
            }

            if (!ExecuteStepAction(merchantRoot, scopeRoot ?? merchantRoot, step, target, anchor, withdrawAmountCoins, recipientPhone, withdrawName, alipayAccount, progress))
            {
                progress.Report($"配置步骤#{step.TargetID} 执行失败（action={step.Action}）。");
                return false;
            }
            var delayAfterStep = step.DelayMs ?? 300;
            delayAfterStep += Random.Shared.Next(300, 3001);
            if (delayAfterStep > 0)
                await Task.Delay(delayAfterStep, ct).ConfigureAwait(false);
        }

        AutomationDevLog.Report(progress, "已按捕捉设置完成商家窗口操作。");
        return true;
    }

    /// <summary>
    /// 签到流程：筛选可提收益 &gt; 10500 的账号，逐个「显示此号」并按签到配置执行商家动作。
    /// </summary>
    public async Task<int> RunDailyCheckAsync(
        IProgress<string> progress,
        IReadOnlyList<CaptureTargetItem> captureTargets,
        CancellationToken cancellationToken = default)
    {
        if (captureTargets == null || captureTargets.Count == 0)
        {
            progress.Report("签到配置为空，未执行。");
            return 0;
        }

        progress.Report("正在启动/附着迅捷小辅助...");
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法找到或启动迅捷小辅助窗口。");
            return 0;
        }
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        progress.Report("正在读取辅助软件账号列表...");
        var raw = HelperGridReader.CollectCandidates(helperRoot, progress, MinDailyCheckScore);
        var candidates = DeduplicateByUsername(raw);
        progress.Report($"符合签到条件的账号数（可提收益>{MinDailyCheckScore}）：{candidates.Count}");
        if (candidates.Count == 0)
            return 0;

        var successCount = 0;
        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"正在处理账号：{c.Username}");

            helperRoot = _automation.LaunchOrAttach(_helperPath) ?? helperRoot;
            var merchantPidsBeforeShow = _automation.EnumerateMainWindowProcessIds(_merchantPath);

            if (!await ShowAccountOnMerchantAsync(helperRoot, c, progress, cancellationToken).ConfigureAwait(false))
                continue;

            var merchantRoot = _automation.AttachMerchantAfterShowAccount(_merchantPath, merchantPidsBeforeShow);
            if (merchantRoot == null)
                continue;
            EnsureWindowNormal(merchantRoot);

            var ok = await ExecuteConfiguredMerchantActionsAsync(
                merchantRoot,
                captureTargets,
                0,
                "",
                "",
                "",
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!ok)
                continue;

            successCount++;
            _automation.MinimizeWindow(merchantRoot);
            await Task.Delay(350, cancellationToken).ConfigureAwait(false);
        }

        progress.Report($"签到流程结束，成功处理账号数：{successCount}。");
        return successCount;
    }

    private bool TryResolveStepTarget(
        AutomationElement merchantRoot,
        CaptureTargetItem step,
        out AutomationElement? target,
        out AutomationElement? anchor,
        out AutomationElement? scopeRoot,
        out string? resolveDebug)
    {
        target = null;
        anchor = null;
        scopeRoot = merchantRoot;
        resolveDebug = null;

        if (string.Equals(step.TargetType, "window", StringComparison.OrdinalIgnoreCase))
        {
            target = merchantRoot;
            anchor = merchantRoot;
            return true;
        }

        if (string.Equals(step.TargetType, "dialog", StringComparison.OrdinalIgnoreCase))
        {
            var dialogRoot = _automation.FindDialogWindow(merchantRoot, step.TargetText ?? "");
            var action = NormalizeAction(step.Action);
            if (dialogRoot == null && action == "solve_math")
            {
                dialogRoot = _automation.FindLikelyMathDialog(merchantRoot);
                if (dialogRoot != null)
                    resolveDebug = $"步骤#{step.TargetID} dialog标题匹配失败，已启用算式弹窗结构兜底并命中。";
            }
            if (dialogRoot == null && !string.IsNullOrWhiteSpace(step.AnchorType))
            {
                dialogRoot = _automation.FindDialogByInnerTarget(merchantRoot, step.AnchorType ?? "", step.AnchorText ?? "");
                if (dialogRoot != null)
                    resolveDebug = $"步骤#{step.TargetID} dialog标题匹配失败，已通过内部目标反查命中（anchorType={step.AnchorType}, anchorText={step.AnchorText}）。";
            }
            if (dialogRoot == null)
            {
                resolveDebug = _automation.BuildDialogSearchDiagnostics(merchantRoot, step.TargetText ?? "");
                return false;
            }

            scopeRoot = dialogRoot;

            if (action == "solve_math")
            {
                if (!string.IsNullOrWhiteSpace(step.AnchorType))
                {
                    var inner = new CaptureTargetItem
                    {
                        TargetType = step.AnchorType ?? "",
                        TargetText = step.AnchorText ?? "",
                        AnchorType = null,
                        AnchorText = null,
                        OffsetX = 0,
                        OffsetY = 0
                    };
                    if (!_automation.TryResolveTarget(inner, dialogRoot, out target) || target == null)
                    {
                        resolveDebug = $"步骤#{step.TargetID} dialog已定位，但未找到锚点目标（anchorType={step.AnchorType}, anchorText={step.AnchorText}）。";
                        return false;
                    }
                }
                else
                {
                    target = _automation.FindFirstEditInSubtree(dialogRoot);
                    if (target == null)
                    {
                        resolveDebug = $"步骤#{step.TargetID} solve_math：dialog已定位，但未找到可输入的Edit控件。";
                        return false;
                    }
                }

                anchor = null;
                return true;
            }

            if (string.IsNullOrWhiteSpace(step.AnchorType))
                return false;

            var innerStep = new CaptureTargetItem
            {
                TargetType = step.AnchorType ?? "",
                TargetText = step.AnchorText ?? "",
                AnchorType = null,
                AnchorText = null,
                OffsetX = 0,
                OffsetY = 0
            };

            if (!_automation.TryResolveTarget(innerStep, dialogRoot, out target) || target == null)
            {
                resolveDebug = $"步骤#{step.TargetID} dialog已定位，但未找到锚点目标（anchorType={step.AnchorType}, anchorText={step.AnchorText}）。";
                return false;
            }

            if (action == "moveTo_click" || action == "moveTo_click_input")
                anchor = target;
            else
                anchor = null;

            return true;
        }

        if (string.IsNullOrWhiteSpace(step.AnchorType))
            return _automation.TryResolveTarget(step, merchantRoot, out target);

        anchor = FindByTypeAndText(merchantRoot, step.AnchorType ?? "", step.AnchorText ?? "");
        if (anchor == null)
            return false;

        return _automation.TryResolveTarget(step, merchantRoot, out target);
    }

    private bool ExecuteStepAction(
        AutomationElement merchantRoot,
        AutomationElement scopeRoot,
        CaptureTargetItem step,
        AutomationElement? target,
        AutomationElement? anchor,
        int withdrawAmountCoins,
        string recipientPhone,
        string withdrawName,
        string alipayAccount,
        IProgress<string> progress)
    {
        var action = NormalizeAction(step.Action);
        if (string.Equals(step.TargetType, "window", StringComparison.OrdinalIgnoreCase))
        {
            var (wx, wy) = GetWindowOffsetPoint(target, step.OffsetX, step.OffsetY);
            if (wx <= 0 || wy <= 0)
                return false;
            _automation.LeftClickAt(wx, wy);
            AutomationDevLog.Report(progress, $"步骤#{step.TargetID} window 偏移动作完成，坐标=({wx},{wy})。");
            if (action != "moveTo_click_input")
                return true;

            var inputValueByWindow = ResolveInputValue(step, withdrawAmountCoins, recipientPhone, withdrawName, alipayAccount);
            if (string.IsNullOrEmpty(inputValueByWindow))
                return false;
            var inputTarget = ResolveInputTargetAtPoint(target, wx, wy);
            if (inputTarget == null)
                return false;
            _automation.SetEditValue(inputTarget, inputValueByWindow);
            AutomationDevLog.Report(progress, $"步骤#{step.TargetID} window 输入完成，值={inputValueByWindow}。");
            return true;
        }

        if (action == "click")
        {
            if (target == null)
                return false;
            _automation.LeftClickElement(target);
            AutomationDevLog.Report(progress, $"步骤#{step.TargetID} click 完成。");
            return true;
        }

        if (action == "click_select")
        {
            if (target == null)
                return false;
            var selectText = ResolveInputValue(step, withdrawAmountCoins, recipientPhone, withdrawName, alipayAccount);
            if (string.IsNullOrWhiteSpace(selectText))
            {
                progress.Report($"配置步骤#{step.TargetID} 动作 click_select 需要填写「输入值」（或与选项文案一致）。");
                return false;
            }

            if (!_automation.TrySelectComboBoxByDisplayText(target, selectText, scopeRoot))
                return false;
            AutomationDevLog.Report(progress, $"步骤#{step.TargetID} click_select 已选择：{selectText}。");
            return true;
        }

        if (action == "solve_math")
        {
            if (target == null || string.Equals(step.TargetType, "dialog", StringComparison.OrdinalIgnoreCase) == false)
                return false;
            if (!AutomationMathExpression.TryEvaluateFromDialogSubtree(scopeRoot, out var computed, out var mathDiag))
            {
                AutomationDevLog.Report(progress, $"步骤#{step.TargetID} {mathDiag}");
                progress.Report($"配置步骤#{step.TargetID} solve_math：未能从对话框 UIA 文本中解析算式。");
                return false;
            }

            _automation.SetEditValue(target, computed.ToString());
            AutomationDevLog.Report(progress, $"步骤#{step.TargetID} solve_math 已填入计算结果：{computed}。");
            return true;
        }

        if (anchor == null)
            return false;
        var (x, y) = GetOffsetPoint(anchor, step.OffsetX, step.OffsetY);
        if (x <= 0 || y <= 0)
            return false;
        _automation.LeftClickAt(x, y);
        AutomationDevLog.Report(progress, $"步骤#{step.TargetID} {action} 偏移点击完成，坐标=({x},{y})。");

        if (action != "moveTo_click_input")
            return true;

        var inputValue = ResolveInputValue(step, withdrawAmountCoins, recipientPhone, withdrawName, alipayAccount);
        if (target == null || string.IsNullOrEmpty(inputValue))
            return false;
        _automation.SetEditValue(target, inputValue);
        AutomationDevLog.Report(progress, $"步骤#{step.TargetID} 输入完成，值={inputValue}。");
        return true;
    }

    private static string NormalizeAction(string? action)
    {
        if (string.Equals(action, "moveTo_click_input", StringComparison.OrdinalIgnoreCase))
            return "moveTo_click_input";
        if (string.Equals(action, "moveTo_click", StringComparison.OrdinalIgnoreCase))
            return "moveTo_click";
        if (string.Equals(action, "click_select", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "click_slecect", StringComparison.OrdinalIgnoreCase))
            return "click_select";
        if (string.Equals(action, "solve_math", StringComparison.OrdinalIgnoreCase))
            return "solve_math";
        return "click";
    }

    private static string ResolveInputValue(
        CaptureTargetItem step,
        int withdrawAmountCoins,
        string recipientPhone,
        string withdrawName,
        string alipayAccount)
    {
        var raw = (step.InputValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            raw = (step.TargetText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        if (raw.StartsWith("{$", StringComparison.Ordinal) && raw.EndsWith("}", StringComparison.Ordinal) && raw.Length > 3)
        {
            var varName = raw.Substring(2, raw.Length - 3).Trim();
            if (string.Equals(varName, "coins", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(varName, "withdrawCoins", StringComparison.OrdinalIgnoreCase))
                return withdrawAmountCoins.ToString();
            if (string.Equals(varName, "phone", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(varName, "recipientPhone", StringComparison.OrdinalIgnoreCase))
                return recipientPhone;
            if (string.Equals(varName, "WithdrawName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(varName, "Name", StringComparison.OrdinalIgnoreCase))
                return withdrawName;
            if (string.Equals(varName, "AlipayAccount", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(varName, "Alipay", StringComparison.OrdinalIgnoreCase))
                return alipayAccount;
            return "";
        }

        if (!raw.Contains('{'))
            return raw;

        var key = $"{step.TargetText} {step.AnchorText}";
        if (key.Contains("手机号", StringComparison.Ordinal))
            return recipientPhone;
        if (key.Contains("兑换", StringComparison.Ordinal) || key.Contains("讯币", StringComparison.Ordinal))
            return withdrawAmountCoins.ToString();
        return raw;
    }

    private AutomationElement? FindByTypeAndText(AutomationElement root, string typeText, string text)
    {
        if (!TryMapControlType(typeText, out var ct))
            return null;
        if (string.IsNullOrWhiteSpace(text))
            return _automation.FindChild(root, ct);
        return _automation.FindDescendantByNameContains(root, ct, text);
    }

    private static (int X, int Y) GetOffsetPoint(AutomationElement anchor, int offsetX, int offsetY)
    {
        try
        {
            var ar = anchor.Current.BoundingRectangle;
            var x = (int)(ar.Left + ar.Width / 2 + offsetX);
            var y = (int)(ar.Top + ar.Height / 2 + offsetY);
            return (x, y);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (int X, int Y) GetWindowOffsetPoint(AutomationElement? window, int offsetX, int offsetY)
    {
        if (window == null)
            return (0, 0);
        try
        {
            var wr = window.Current.BoundingRectangle;
            var x = (int)(wr.Left + offsetX);
            var y = (int)(wr.Top + offsetY);
            return (x, y);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static AutomationElement? ResolveInputTargetAtPoint(AutomationElement? windowRoot, int x, int y)
    {
        // 1) 点击后优先取焦点控件（很多窗口会把焦点给到编辑框）。
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null && focused.Current.ControlType == ControlType.Edit)
                return focused;
        }
        catch { /* ignore */ }

        // 2) 再按命中点向父级回溯查找 Edit。
        try
        {
            var hit = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (hit == null)
                goto fallbackNearest;
            if (hit.Current.ControlType == ControlType.Edit)
                return hit;
            AutomationElement? cur = hit;
            while (cur != null)
            {
                if (cur.Current.ControlType == ControlType.Edit)
                    return cur;
                cur = TreeWalker.ControlViewWalker.GetParent(cur);
            }
        }
        catch { /* ignore */ }

fallbackNearest:
        // 3) 最后从窗口内找“离点击点最近”的输入框做兜底。
        if (windowRoot == null)
            return null;
        try
        {
            var edits = windowRoot.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edits == null || edits.Count == 0)
                return null;

            AutomationElement? best = null;
            var bestScore = double.MaxValue;
            foreach (AutomationElement e in edits)
            {
                try
                {
                    var r = e.Current.BoundingRectangle;
                    if (r.Width <= 0 || r.Height <= 0)
                        continue;
                    var cx = r.Left + r.Width / 2.0;
                    var cy = r.Top + r.Height / 2.0;
                    var dx = cx - x;
                    var dy = cy - y;
                    var dist2 = dx * dx + dy * dy;
                    if (dist2 < bestScore)
                    {
                        bestScore = dist2;
                        best = e;
                    }
                }
                catch { /* ignore */ }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryMapControlType(string? typeText, out ControlType controlType)
    {
        controlType = ControlType.Text;
        if (string.IsNullOrWhiteSpace(typeText) ||
            string.Equals(typeText, "文字", StringComparison.Ordinal) ||
            string.Equals(typeText, "text", StringComparison.OrdinalIgnoreCase))
        {
            controlType = ControlType.Text;
            return true;
        }
        if (string.Equals(typeText, "输入框", StringComparison.Ordinal) ||
            string.Equals(typeText, "inputBox", StringComparison.OrdinalIgnoreCase))
        {
            controlType = ControlType.Edit;
            return true;
        }
        if (string.Equals(typeText, "按钮", StringComparison.Ordinal) ||
            string.Equals(typeText, "button", StringComparison.OrdinalIgnoreCase))
        {
            controlType = ControlType.Button;
            return true;
        }
        if (string.Equals(typeText, "radioBtn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeText, "单选框", StringComparison.Ordinal))
        {
            controlType = ControlType.RadioButton;
            return true;
        }
        if (string.Equals(typeText, "dropList", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeText, "下拉框", StringComparison.Ordinal) ||
            string.Equals(typeText, "下拉列表", StringComparison.Ordinal))
        {
            controlType = ControlType.ComboBox;
            return true;
        }
        if (string.Equals(typeText, "window", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeText, "窗口", StringComparison.Ordinal))
        {
            controlType = ControlType.Window;
            return true;
        }
        return false;
    }

    private static List<WithdrawCandidateRow> DeduplicateByUsername(List<WithdrawCandidateRow> rows)
    {
        var map = new Dictionary<string, WithdrawCandidateRow>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (!map.TryGetValue(r.Username, out var old) || r.Score > old.Score)
                map[r.Username] = r;
        }
        return map.Values.ToList();
    }

    private static void EnsureWindowNormal(AutomationElement window)
    {
        try
        {
            if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var wpObj) && wpObj is WindowPattern wp)
                wp.SetWindowVisualState(WindowVisualState.Normal);
        }
        catch { /* ignore */ }
    }

    private async Task<bool> ShowAccountOnMerchantAsync(
        AutomationElement helperRoot,
        WithdrawCandidateRow candidate,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var row = HelperGridReader.FindDataItemRowByUsername(helperRoot, candidate.Username);
        if (row == null)
        {
            progress.Report($"未在表格中找到用户行（rowID={candidate.RowId}）：{candidate.Username}");
            return false;
        }

        if (_automation.IsElementVisibleInViewport(row))
        {
            AutomationDevLog.Report(progress, $"rowID={candidate.RowId} 已在可视范围内，无需滚动。");
        }
        else
        {
            var ensuredVisible = _automation.TryEnsureDataItemVisible(row);
            AutomationDevLog.Report(progress, ensuredVisible
                ? $"rowID={candidate.RowId} 原本不可视，已滚动到可视范围。"
                : $"rowID={candidate.RowId} 不可视且滚动未能保证可视，继续尝试直接操作。");
        }

        _automation.LeftClickElement(row);
        var (clickX, clickY) = GetElementCenter(row);
        AutomationDevLog.Report(progress, $"rowID={candidate.RowId} 已左键选中目标行，坐标=({clickX},{clickY})。");
        await Task.Delay(80, ct).ConfigureAwait(false);

        _automation.RightClickElement(row);
        AutomationDevLog.Report(progress, $"rowID={candidate.RowId} 已在目标行执行右键，坐标=({clickX},{clickY})。");
        await Task.Delay(280, ct).ConfigureAwait(false);
        var menuItem = _automation.FindMenuItem("显示此号");
        if (menuItem == null)
        {
            progress.Report($"未找到右键菜单项「显示此号」（rowID={candidate.RowId}，用户名={candidate.Username}）。");
            return false;
        }
        _automation.InvokeButton(menuItem);
        await Task.Delay(900, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> MerchantFillExchangeAsync(
        AutomationElement merchantRoot,
        int withdrawAmountCoins,
        IProgress<string> progress,
        CancellationToken ct)
    {
        if (!SelectTabItem(merchantRoot, "讯币兑现", progress))
        {
            progress.Report("未能切换到「讯币兑现」页面。");
            return false;
        }
        await Task.Delay(350, ct).ConfigureAwait(false);

        var scope = _automation.FindDescendantByNameContains(merchantRoot, ControlType.Group, "积分换讯币")
            ?? _automation.FindDescendantByNameContains(merchantRoot, ControlType.Pane, "积分换讯币")
            ?? merchantRoot;

        var edit = FindExchangeQuantityEdit(scope);
        if (edit == null)
        {
            progress.Report("未找到「兑换讯币数量」输入框。");
            return false;
        }
        _automation.SetEditValue(edit, withdrawAmountCoins.ToString());
        await Task.Delay(200, ct).ConfigureAwait(false);

        var ok = FindNamedButtonInScope(scope, "确定");
        if (ok == null)
        {
            progress.Report("未找到讯币兑现区域的「确定」按钮。");
            return false;
        }
        _automation.InvokeButton(ok);
        AutomationDevLog.Report(progress, "已提交讯币兑现。");
        return true;
    }

    private AutomationElement? FindExchangeQuantityEdit(AutomationElement scope)
    {
        var byName = _automation.FindDescendantByNameContains(scope, ControlType.Edit, "兑换讯币数量");
        if (byName != null)
            return byName;
        try
        {
            var edits = scope.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edits == null || edits.Count == 0)
                return null;
            if (edits.Count == 1)
                return edits[0];
            foreach (AutomationElement e in edits)
            {
                try
                {
                    var n = e.Current.Name ?? "";
                    if (n.Contains("兑换", StringComparison.Ordinal))
                        return e;
                }
                catch { /* ignore */ }
            }
            return edits[0];
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> MerchantFillMemberCenterAsync(
        AutomationElement merchantRoot,
        string phone,
        int amountCoins,
        IProgress<string> progress,
        CancellationToken ct)
    {
        if (!SelectTabItem(merchantRoot, "会员中心", progress))
        {
            progress.Report("未能切换到「会员中心」页面。");
            return false;
        }
        await Task.Delay(350, ct).ConfigureAwait(false);

        var scope = _automation.FindDescendantByNameContains(merchantRoot, ControlType.Group, "给APP手机号转账")
            ?? _automation.FindDescendantByNameContains(merchantRoot, ControlType.Pane, "给APP手机号转账")
            ?? _automation.FindDescendantByNameContains(merchantRoot, ControlType.Custom, "手机号转账")
            ?? merchantRoot;

        var edits = CollectEditsByTop(scope);
        if (edits.Count < 3)
        {
            progress.Report($"会员中心转账区输入框数量不足（需要 3 个，当前 {edits.Count}）。");
            return false;
        }

        var amt = amountCoins.ToString();
        _automation.SetEditValue(edits[0], phone);
        await Task.Delay(120, ct).ConfigureAwait(false);
        _automation.SetEditValue(edits[1], amt);
        await Task.Delay(120, ct).ConfigureAwait(false);
        _automation.SetEditValue(edits[2], amt);
        await Task.Delay(200, ct).ConfigureAwait(false);

        var ok = FindNamedButtonInScope(scope, "确定");
        if (ok == null)
        {
            progress.Report("未找到会员中心转账区的「确定」按钮。");
            return false;
        }
        _automation.InvokeButton(ok);
        AutomationDevLog.Report(progress, "已提交会员中心转账。");
        return true;
    }

    private static List<AutomationElement> CollectEditsByTop(AutomationElement scope)
    {
        var list = new List<AutomationElement>();
        AutomationElementCollection? edits = null;
        try
        {
            edits = scope.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        }
        catch
        {
            return list;
        }
        if (edits == null)
            return list;
        foreach (AutomationElement e in edits)
            list.Add(e);
        list.Sort((a, b) =>
        {
            try
            {
                return a.Current.BoundingRectangle.Top.CompareTo(b.Current.BoundingRectangle.Top);
            }
            catch
            {
                return 0;
            }
        });
        return list;
    }

    private bool SelectTabItem(AutomationElement root, string nameContains, IProgress<string> progress)
    {
        // 策略A：标准 TabItem
        var tab = _automation.FindDescendantByNameContains(root, ControlType.TabItem, nameContains);
        if (tab != null)
        {
            try
            {
                if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var spObj) && spObj is SelectionItemPattern sip)
                    sip.Select();
                else
                    _automation.InvokeButton(tab);
                AutomationDevLog.Report(progress, $"已通过 TabItem 切换到「{nameContains}」。");
                return true;
            }
            catch
            {
                try
                {
                    _automation.InvokeButton(tab);
                    AutomationDevLog.Report(progress, $"已通过 TabItem(Invoke) 切换到「{nameContains}」。");
                    return true;
                }
                catch { /* ignore */ }
            }
        }

        // 策略B：按钮文本匹配
        var btn = _automation.FindDescendantByNameContains(root, ControlType.Button, nameContains);
        if (btn != null)
        {
            try
            {
                _automation.LeftClickElement(btn);
                AutomationDevLog.Report(progress, $"已通过 Button 文本匹配切换到「{nameContains}」。");
                return true;
            }
            catch { /* ignore */ }

            try
            {
                _automation.InvokeButton(btn);
                AutomationDevLog.Report(progress, $"已通过 Button Invoke 切换到「{nameContains}」。");
                return true;
            }
            catch { /* ignore */ }
        }

        // 策略C：文本/自定义控件命中，点击自身或父容器
        var textOrCustom = _automation.FindDescendantByNameContains(root, ControlType.Text, nameContains)
            ?? _automation.FindDescendantByNameContains(root, ControlType.Custom, nameContains);
        if (textOrCustom != null)
        {
            try
            {
                _automation.LeftClickElement(textOrCustom);
                AutomationDevLog.Report(progress, $"已通过 Text/Custom 命中切换到「{nameContains}」。");
                return true;
            }
            catch { /* ignore */ }
            try
            {
                var parent = TreeWalker.ControlViewWalker.GetParent(textOrCustom);
                if (parent != null)
                {
                    _automation.LeftClickElement(parent);
                    AutomationDevLog.Report(progress, $"已通过 Text/Custom 父容器点击切换到「{nameContains}」。");
                    return true;
                }
            }
            catch { /* ignore */ }
        }

        // 策略D：锚点偏移（以「会员中心」作为参照，估算顶部标签位置）
        var anchor = _automation.FindDescendantByNameContains(root, ControlType.Text, "会员中心")
            ?? _automation.FindDescendantByNameContains(root, ControlType.Button, "会员中心")
            ?? _automation.FindDescendantByNameContains(root, ControlType.Custom, "会员中心");
        if (anchor != null)
        {
            var (ax, ay) = GetElementCenter(anchor);
            if (ax > 0 && ay > 0)
            {
                // 「讯币兑现」一般在「会员中心」左侧一个标签位；「会员中心」本身则不偏移。
                var tx = string.Equals(nameContains, "会员中心", StringComparison.Ordinal)
                    ? ax
                    : ax - 110;
                var ty = ay;
                _automation.LeftClickAt(tx, ty);
                AutomationDevLog.Report(progress, $"已通过锚点偏移点击切换到「{nameContains}」，点击坐标=({tx},{ty})。");
                return true;
            }
        }

        // 策略E：窗口相对偏移兜底（顶部导航大致区域）
        try
        {
            var wr = root.Current.BoundingRectangle;
            if (wr.Width > 0 && wr.Height > 0)
            {
                // 估算：顶部导航条 y≈40；x 按名称选择经验值。
                var y = (int)(wr.Top + 40);
                var x = string.Equals(nameContains, "会员中心", StringComparison.Ordinal)
                    ? (int)(wr.Left + 470)
                    : (int)(wr.Left + 380);
                _automation.LeftClickAt(x, y);
                AutomationDevLog.Report(progress, $"已通过窗口偏移兜底点击切换到「{nameContains}」，点击坐标=({x},{y})。");
                return true;
            }
        }
        catch { /* ignore */ }

        progress.Report($"未找到可用策略切换到「{nameContains}」（TabItem/Button/Text/锚点偏移/窗口偏移均失败）。");
        return false;
    }

    private AutomationElement? FindNamedButtonInScope(AutomationElement scope, string name)
    {
        try
        {
            var cond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, name));
            return scope.FindFirst(TreeScope.Descendants, cond);
        }
        catch
        {
            return null;
        }
    }

    private static (int X, int Y) GetElementCenter(AutomationElement element)
    {
        try
        {
            var r = element.Current.BoundingRectangle;
            return ((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
        }
        catch
        {
            return (0, 0);
        }
    }
}
