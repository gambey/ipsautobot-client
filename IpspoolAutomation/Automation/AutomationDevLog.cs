using System.Diagnostics;

namespace IpspoolAutomation.Automation;

/// <summary>
/// 仅在 DEBUG 编译时向进度回调输出详细诊断日志；Release 正式版中调用会被编译器剔除。
/// </summary>
internal static class AutomationDevLog
{
    [Conditional("DEBUG")]
    public static void Report(IProgress<string>? progress, string message)
    {
        progress?.Report(message);
    }
}
