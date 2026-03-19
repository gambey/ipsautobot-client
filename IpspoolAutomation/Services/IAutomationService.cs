using System.Windows.Automation;

namespace IpspoolAutomation.Services;

public interface IAutomationService
{
    AutomationElement? LaunchOrAttach(string exePath, int waitMs = 5000);
    AutomationElement? FindChild(AutomationElement root, ControlType controlType, string? name = null);
    void InvokeButton(AutomationElement element);
    void SetEditValue(AutomationElement element, string value);
}
