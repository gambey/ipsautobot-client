using System.Windows.Automation;
using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public interface IAutomationService
{
    AutomationElement? LaunchOrAttach(string exePath, int waitMs = 5000);

    /// <summary>当前已存在、且具备主窗口的商家版进程 Id（用于「显示此号」前后对比）。</summary>
    IReadOnlyList<int> EnumerateMainWindowProcessIds(string exePath);

    /// <summary>在辅助端执行「显示此号」后附着对应商家版窗口：优先新进程，其次前台窗口为商家 exe 时附着。</summary>
    AutomationElement? AttachMerchantAfterShowAccount(string exePath, IReadOnlyCollection<int> processIdsBeforeShowAccount, int waitMs = 8000);
    AutomationElement? FindChild(AutomationElement root, ControlType controlType, string? name = null);
    AutomationElementCollection FindAll(AutomationElement root, ControlType controlType, TreeScope scope = TreeScope.Descendants);
    AutomationElement? FindDescendantByNameContains(AutomationElement root, ControlType controlType, string nameContains);
    bool TryResolveTarget(CaptureTargetItem item, AutomationElement root, out AutomationElement? target);
    AutomationElement? FindMenuItem(string name);
    void InvokeButton(AutomationElement element);
    void SetEditValue(AutomationElement element, string value);
    void SetFocus(AutomationElement element);
    bool TryGetValue(AutomationElement element, out string value);
    bool MinimizeWindow(AutomationElement windowElement);
    bool IsElementVisibleInViewport(AutomationElement element);
    bool TryEnsureDataItemVisible(AutomationElement dataItem, int maxSteps = 20);
    bool TryBringDataItemToTop(AutomationElement dataItem, int maxSteps = 20);
    bool TryScrollToBottom(AutomationElement contextElement);
    void LeftClickElement(AutomationElement element);
    void LeftClickAt(int x, int y);
    void RightClickElement(AutomationElement element);
}
