using System.Diagnostics;
using System.Windows.Automation;
using System.IO;
namespace IpspoolAutomation.Services;

public sealed class UIAutomationService : IAutomationService
{
    public AutomationElement? LaunchOrAttach(string exePath, int waitMs = 5000)
    {
        if (!File.Exists(exePath))
            return null;
        var fileName = Path.GetFileNameWithoutExtension(exePath);
        var processes = Process.GetProcessesByName(fileName);
        if (processes.Length == 0)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch
            {
                return null;
            }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < waitMs)
            {
                Thread.Sleep(200);
                processes = Process.GetProcessesByName(fileName);
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            var el = AutomationElement.FromHandle(p.MainWindowHandle);
                            if (el != null)
                                return el;
                        }
                    }
                    catch { /* ignore */ }
                }
            }
            return null;
        }
        foreach (var p in processes)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    var el = AutomationElement.FromHandle(p.MainWindowHandle);
                    if (el != null)
                        return el;
                }
            }
            catch { /* ignore */ }
        }
        return null;
    }

    public AutomationElement? FindChild(AutomationElement root, ControlType controlType, string? name = null)
    {
        Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
        if (!string.IsNullOrEmpty(name))
            cond = new AndCondition(cond, new PropertyCondition(AutomationElement.NameProperty, name));
        return root.FindFirst(TreeScope.Descendants, cond);
    }

    public void InvokeButton(AutomationElement element)
    {
        var invokePattern = element.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        invokePattern?.Invoke();
    }

    public void SetEditValue(AutomationElement element, string value)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) && patternObj is ValuePattern valuePattern)
        {
            valuePattern.SetValue(value);
        }
    }
}
