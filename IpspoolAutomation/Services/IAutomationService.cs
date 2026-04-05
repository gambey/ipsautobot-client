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

    /// <summary>
    /// 在桌面树中查找标题名包含 <paramref name="titleContains"/> 的顶层窗口，优先与 <paramref name="merchantRoot"/> 同进程。
    /// </summary>
    AutomationElement? FindDialogWindow(AutomationElement merchantRoot, string titleContains);

    /// <summary>在子树中查找第一个 <see cref="ControlType.Edit"/>（用于算式对话框等）。</summary>
    AutomationElement? FindFirstEditInSubtree(AutomationElement root);
    /// <summary>按结构特征（Edit + 确定/取消按钮 + 算式文本）兜底查找算式弹窗。</summary>
    AutomationElement? FindLikelyMathDialog(AutomationElement merchantRoot);
    /// <summary>当标题匹配失败时，按对话框内部目标（如 button/确定）反查弹窗容器。</summary>
    AutomationElement? FindDialogByInnerTarget(AutomationElement merchantRoot, string targetType, string targetText);
    /// <summary>输出弹窗定位诊断信息（候选标题/进程/可见性），用于日志排查。</summary>
    string BuildDialogSearchDiagnostics(AutomationElement merchantRoot, string titleContains, int maxItems = 8);

    bool TryResolveTarget(CaptureTargetItem item, AutomationElement root, out AutomationElement? target);
    AutomationElement? FindMenuItem(string name);
    void InvokeButton(AutomationElement element);
    void SetEditValue(AutomationElement element, string value);
    void SetFocus(AutomationElement element);
    bool TryGetValue(AutomationElement element, out string value);
    bool MinimizeWindow(AutomationElement windowElement);
    bool IsElementVisibleInViewport(AutomationElement element);
    bool TryEnsureDataItemVisible(AutomationElement dataItem, int maxSteps = 20);
    /// <summary>将表格行滚入「足够」可视区（避开横向滚动条压行），便于右键弹出「显示此号」。</summary>
    bool TryEnsureGridRowReadyForContextMenu(AutomationElement dataItem, int maxSteps = 28);
    /// <summary>计算行在滚动宿主安全区域内的点击点（行矩形与可视区交集的中心）；失败时返回 false。</summary>
    bool TryGetGridRowContextClickPoint(AutomationElement row, out int x, out int y);
    void LeftClickGridRowForContextMenu(AutomationElement row);
    void RightClickGridRowForContextMenu(AutomationElement row);
    bool TryBringDataItemToTop(AutomationElement dataItem, int maxSteps = 20);
    bool TryScrollToBottom(AutomationElement contextElement);
    void LeftClickElement(AutomationElement element);
    void LeftClickAt(int x, int y);
    void RightClickElement(AutomationElement element);

    /// <summary>
    /// 在下拉框中选择显示文本与 <paramref name="displayText"/> 匹配的项（先 ValuePattern，再展开后选 ListItem）。
    /// </summary>
    bool TrySelectComboBoxByDisplayText(AutomationElement combo, string displayText, AutomationElement? searchWithin = null);
}
