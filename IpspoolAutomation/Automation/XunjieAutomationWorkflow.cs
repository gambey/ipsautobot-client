using System.Windows.Automation;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.Automation;

public class XunjieAutomationWorkflow
{
    private readonly IAutomationService _automation;
    private readonly string _helperPath;
    private readonly string _merchantPath;

    public XunjieAutomationWorkflow(IAutomationService automation, string helperPath, string merchantPath)
    {
        _automation = automation;
        _helperPath = helperPath;
        _merchantPath = merchantPath;
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
}
