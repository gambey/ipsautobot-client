using System.Windows.Automation;
using IpspoolAutomation.Models.Capture;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.Automation;

/// <summary>停止平台单：按账号列表逐个「显示此号」并在商家端执行配置步骤。</summary>
public sealed class StopPlatformOrderingWorkflow
{
    private readonly IAutomationService _automation;
    private readonly string _helperPath;
    private readonly string _merchantPath;
    private readonly XunjieAutomationWorkflow _wf;

    public StopPlatformOrderingWorkflow(IAutomationService automation, string helperPath, string merchantPath)
    {
        _automation = automation;
        _helperPath = helperPath;
        _merchantPath = merchantPath;
        _wf = new XunjieAutomationWorkflow(automation, helperPath, merchantPath);
    }

    public async Task<int> RunAsync(
        IReadOnlyList<WithdrawCandidateRow> candidates,
        IReadOnlyList<CaptureTargetItem> steps,
        IProgress<string> progress,
        CancellationToken ct,
        string flowDisplayName = "停止平台单",
        string? paymentPassword = null)
    {
        if (steps == null || steps.Count == 0)
        {
            progress.Report($"{flowDisplayName}：自动化步骤为空，未执行。");
            return 0;
        }

        if (candidates.Count == 0)
        {
            progress.Report("没有可处理的账号。");
            return 0;
        }

        progress.Report($"{flowDisplayName}：将依次处理 {candidates.Count} 个账号。");
        var helperRoot = _automation.LaunchOrAttach(_helperPath);
        if (helperRoot == null)
        {
            progress.Report("无法启动或附着迅捷辅助窗口。");
            return 0;
        }

        await Task.Delay(400, ct).ConfigureAwait(false);

        var successCount = 0;
        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report($"正在处理账号：{c.Username}（rowID={c.RowId}）");

            helperRoot = _automation.LaunchOrAttach(_helperPath) ?? helperRoot;
            var merchantPidsBefore = _automation.EnumerateMainWindowProcessIds(_merchantPath);

            if (!await _wf.ShowHelperAccountForAutomationAsync(helperRoot, c, progress, ct).ConfigureAwait(false))
                continue;

            var merchantRoot = _automation.AttachMerchantAfterShowAccount(_merchantPath, merchantPidsBefore);
            if (merchantRoot == null)
            {
                progress.Report($"未能附着商家版窗口，跳过：{c.Username}");
                continue;
            }

            EnsureWindowNormal(merchantRoot);

            var ok = await _wf.ExecuteCaptureStepsAsync(merchantRoot, steps, progress, ct, paymentPassword).ConfigureAwait(false);
            if (!ok)
            {
                progress.Report($"商家端步骤未完成，跳过：{c.Username}");
                _automation.MinimizeWindow(merchantRoot);
                await Task.Delay(300, ct).ConfigureAwait(false);
                continue;
            }

            successCount++;
            _automation.MinimizeWindow(merchantRoot);
            await Task.Delay(350, ct).ConfigureAwait(false);
        }

        progress.Report($"{flowDisplayName}流程结束，成功处理账号数：{successCount}。");
        return successCount;
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
