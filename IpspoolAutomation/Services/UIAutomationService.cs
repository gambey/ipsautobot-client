using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public sealed class UIAutomationService : IAutomationService
{
    public IReadOnlyList<int> EnumerateMainWindowProcessIds(string exePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(exePath);
        var list = new List<int>();
        if (string.IsNullOrEmpty(fileName))
            return list;
        try
        {
            foreach (var p in Process.GetProcessesByName(fileName))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        list.Add(p.Id);
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        return list;
    }

    /// <summary>
    /// 「显示此号」后：优先附着新启动的商家进程；若无新进程则尝试前台窗口为同一商家 exe；
    /// 仍失败则回退到 <see cref="LaunchOrAttach"/>。
    /// </summary>
    public AutomationElement? AttachMerchantAfterShowAccount(string exePath, IReadOnlyCollection<int> processIdsBeforeShowAccount, int waitMs = 8000)
    {
        if (!File.Exists(exePath))
            return null;
        var fileName = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrEmpty(fileName))
            return null;
        var before = new HashSet<int>(processIdsBeforeShowAccount);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitMs)
        {
            foreach (var p in Process.GetProcessesByName(fileName))
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero)
                        continue;
                    if (!before.Contains(p.Id))
                        return AutomationElement.FromHandle(p.MainWindowHandle);
                }
                catch { /* ignore */ }
            }

            var fg = NativeInput.GetForegroundWindow();
            if (fg != IntPtr.Zero && NativeInput.GetWindowThreadProcessId(fg, out var pidU) != 0)
            {
                if (IsSameMerchantExe((int)pidU, exePath))
                    return AutomationElement.FromHandle(fg);
            }
            Thread.Sleep(150);
        }

        var fgLast = NativeInput.GetForegroundWindow();
        if (fgLast != IntPtr.Zero && NativeInput.GetWindowThreadProcessId(fgLast, out var pidLast) != 0)
        {
            if (IsSameMerchantExe((int)pidLast, exePath))
                return AutomationElement.FromHandle(fgLast);
        }
        return LaunchOrAttach(exePath, 3000);
    }

    private static bool IsSameMerchantExe(int processId, string expectedExePath)
    {
        var expectedName = Path.GetFileNameWithoutExtension(expectedExePath);
        try
        {
            using var p = Process.GetProcessById(processId);
            string? path;
            try
            {
                path = p.MainModule?.FileName;
            }
            catch
            {
                return false;
            }
            if (string.IsNullOrEmpty(path))
                return false;
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), expectedName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!File.Exists(expectedExePath))
                return true;
            try
            {
                return string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedExePath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

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
        System.Windows.Automation.Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
        if (!string.IsNullOrEmpty(name))
            cond = new AndCondition(cond, new PropertyCondition(AutomationElement.NameProperty, name));
        return root.FindFirst(TreeScope.Descendants, cond);
    }

    public AutomationElementCollection FindAll(AutomationElement root, ControlType controlType, TreeScope scope = TreeScope.Descendants)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
        return root.FindAll(scope, cond);
    }

    public AutomationElement? FindDescendantByNameContains(AutomationElement root, ControlType controlType, string nameContains)
    {
        AutomationElementCollection all;
        try
        {
            all = FindAll(root, controlType, TreeScope.Descendants);
        }
        catch
        {
            return null;
        }
        foreach (AutomationElement el in all)
        {
            try
            {
                var n = el.Current.Name ?? "";
                if (n.Contains(nameContains, StringComparison.Ordinal))
                    return el;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    public bool TryResolveTarget(CaptureTargetItem item, AutomationElement root, out AutomationElement? target)
    {
        target = null;
        if (item == null || root == null)
            return false;

        var hasAnchor = !string.IsNullOrWhiteSpace(item.AnchorType);
        if (!hasAnchor)
        {
            return TryFindByTypeAndText(root, item.TargetType, item.TargetText, out target);
        }

        if (!TryFindByTypeAndText(root, item.AnchorType ?? "", item.AnchorText ?? "", out var anchor) || anchor == null)
            return false;

        try
        {
            var ar = anchor.Current.BoundingRectangle;
            var px = ar.Left + ar.Width / 2 + item.OffsetX;
            var py = ar.Top + ar.Height / 2 + item.OffsetY;
            var hit = AutomationElement.FromPoint(new System.Windows.Point(px, py));
            if (hit == null)
                return false;

            if (!TryMapControlType(item.TargetType, out var targetCt))
            {
                target = hit;
                return true;
            }

            AutomationElement? cur = hit;
            while (cur != null)
            {
                try
                {
                    if (cur.Current.ControlType == targetCt)
                    {
                        var expectedText = item.TargetText ?? "";
                        if (string.IsNullOrWhiteSpace(expectedText) ||
                            (cur.Current.Name ?? "").Contains(expectedText, StringComparison.Ordinal))
                        {
                            target = cur;
                            return true;
                        }
                    }
                }
                catch { /* ignore */ }
                try
                {
                    cur = TreeWalker.ControlViewWalker.GetParent(cur);
                }
                catch
                {
                    break;
                }
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    public AutomationElement? FindMenuItem(string name)
    {
        try
        {
            var cond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.NameProperty, name));
            return AutomationElement.RootElement.FindFirst(TreeScope.Descendants, cond);
        }
        catch
        {
            return null;
        }
    }

    public void InvokeButton(AutomationElement element)
    {
        var invokePattern = element.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        invokePattern?.Invoke();
    }

    public void SetEditValue(AutomationElement element, string value)
    {
        SetFocus(element);
        Thread.Sleep(30);
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) && patternObj is ValuePattern valuePattern)
        {
            valuePattern.SetValue(value);
            return;
        }
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj) && tpObj is TextPattern textPattern)
        {
            /* read-only text */
        }
    }

    public void SetFocus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
        }
        catch
        {
            try
            {
                NativeInput.LeftClickCenter(element);
            }
            catch { /* ignore */ }
        }
    }

    public bool TryGetValue(AutomationElement element, out string value)
    {
        value = "";
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vo) && vo is ValuePattern vp)
            {
                value = vp.Current.Value ?? "";
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    public bool MinimizeWindow(AutomationElement windowElement)
    {
        if (windowElement == null)
            return false;
        try
        {
            if (windowElement.TryGetCurrentPattern(WindowPattern.Pattern, out var wpObj) && wpObj is WindowPattern wp)
            {
                wp.SetWindowVisualState(WindowVisualState.Minimized);
                Thread.Sleep(80);
                try
                {
                    if (wp.Current.WindowVisualState == WindowVisualState.Minimized)
                        return true;
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }

        // UIA 失败时，使用 Win32 兜底。
        try
        {
            var hwnd = new IntPtr(windowElement.Current.NativeWindowHandle);
            if (hwnd == IntPtr.Zero)
                return false;
            _ = NativeInput.ShowWindow(hwnd, NativeInput.SwMinimize);
            Thread.Sleep(80);
            return NativeInput.IsIconic(hwnd);
        }
        catch
        {
            return false;
        }
    }

    public bool IsElementVisibleInViewport(AutomationElement element)
    {
        if (element == null)
            return false;
        try
        {
            if (element.Current.IsOffscreen)
                return false;
        }
        catch
        {
            return false;
        }

        AutomationElement? container;
        double targetLeft;
        double targetTop;
        double targetWidth;
        double targetHeight;
        try
        {
            var tr = element.Current.BoundingRectangle;
            targetLeft = tr.Left;
            targetTop = tr.Top;
            targetWidth = tr.Width;
            targetHeight = tr.Height;
            container = FindScrollableAncestor(element);
        }
        catch
        {
            return false;
        }
        if (targetWidth <= 0 || targetHeight <= 0)
            return false;

        if (container == null)
            return true;

        double viewportLeft;
        double viewportTop;
        double viewportRight;
        double viewportBottom;
        try
        {
            var vr = container.Current.BoundingRectangle;
            viewportLeft = vr.Left;
            viewportTop = vr.Top;
            viewportRight = vr.Right;
            viewportBottom = vr.Bottom;
        }
        catch
        {
            return true;
        }

        // 中心点在可滚动容器的可视矩形内，认为可见。
        var cx = targetLeft + targetWidth / 2;
        var cy = targetTop + targetHeight / 2;
        return cx >= viewportLeft && cx <= viewportRight && cy >= viewportTop && cy <= viewportBottom;
    }

    public bool TryEnsureDataItemVisible(AutomationElement dataItem, int maxSteps = 20)
    {
        if (dataItem == null)
            return false;
        if (IsElementVisibleInViewport(dataItem))
            return true;

        try
        {
            if (dataItem.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var siObj) && siObj is ScrollItemPattern sip)
            {
                sip.ScrollIntoView();
                Thread.Sleep(60);
                if (IsElementVisibleInViewport(dataItem))
                    return true;
            }
        }
        catch { /* ignore */ }

        var scrollContainer = FindScrollableAncestor(dataItem);
        if (scrollContainer == null)
            return false;
        if (!scrollContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj) || spObj is not ScrollPattern sp)
            return IsElementVisibleInViewport(dataItem);
        if (!sp.Current.VerticallyScrollable)
            return IsElementVisibleInViewport(dataItem);

        for (var i = 0; i < maxSteps; i++)
        {
            if (IsElementVisibleInViewport(dataItem))
                return true;

            try
            {
                var rowRect = dataItem.Current.BoundingRectangle;
                var viewport = scrollContainer.Current.BoundingRectangle;
                var rowCenterY = rowRect.Top + rowRect.Height / 2;
                var viewportCenterY = viewport.Top + viewport.Height / 2;
                var amount = rowCenterY < viewportCenterY ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement;
                sp.ScrollVertical(amount);
            }
            catch
            {
                return IsElementVisibleInViewport(dataItem);
            }
            Thread.Sleep(40);
        }
        return IsElementVisibleInViewport(dataItem);
    }

    public bool TryBringDataItemToTop(AutomationElement dataItem, int maxSteps = 20)
    {
        if (dataItem == null || maxSteps <= 0)
            return false;
        var scrollContainer = FindScrollableAncestor(dataItem);
        if (scrollContainer == null)
            return false;
        if (!scrollContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj) || spObj is not ScrollPattern sp)
            return false;

        try
        {
            if (dataItem.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var siObj) && siObj is ScrollItemPattern sip)
                sip.ScrollIntoView();
        }
        catch { /* ignore */ }

        for (var i = 0; i < maxSteps; i++)
        {
            double rowTop;
            double containerTop;
            try
            {
                rowTop = dataItem.Current.BoundingRectangle.Top;
                containerTop = scrollContainer.Current.BoundingRectangle.Top;
            }
            catch
            {
                return false;
            }
            if (rowTop <= containerTop + 6)
                return true;

            try
            {
                if (!sp.Current.VerticallyScrollable)
                    return false;
                if (sp.Current.VerticalScrollPercent <= 0)
                    return false;
                sp.ScrollVertical(ScrollAmount.SmallDecrement);
            }
            catch
            {
                return false;
            }
            Thread.Sleep(45);
        }

        try
        {
            return dataItem.Current.BoundingRectangle.Top <= scrollContainer.Current.BoundingRectangle.Top + 6;
        }
        catch
        {
            return false;
        }
    }

    public bool TryScrollToBottom(AutomationElement contextElement)
    {
        var scrollContainer = FindScrollableAncestor(contextElement);
        if (scrollContainer == null)
            return false;
        if (!scrollContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj) || spObj is not ScrollPattern sp)
            return false;

        try
        {
            if (!sp.Current.VerticallyScrollable)
                return false;
            var h = sp.Current.HorizontallyScrollable ? sp.Current.HorizontalScrollPercent : ScrollPattern.NoScroll;
            sp.SetScrollPercent(h, 100);
            Thread.Sleep(60);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void LeftClickElement(AutomationElement element)
    {
        NativeInput.LeftClickCenter(element);
    }

    public void LeftClickAt(int x, int y)
    {
        NativeInput.LeftClickAt(x, y);
    }

    public void RightClickElement(AutomationElement element)
    {
        NativeInput.RightClickCenter(element);
    }

    public bool TrySelectComboBoxByDisplayText(AutomationElement combo, string displayText, AutomationElement? searchWithin = null)
    {
        if (combo == null || string.IsNullOrWhiteSpace(displayText))
            return false;
        displayText = displayText.Trim();

        try
        {
            if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
            {
                try
                {
                    vp.SetValue(displayText);
                    Thread.Sleep(40);
                    var cur = vp.Current.Value ?? "";
                    if (string.Equals(cur.Trim(), displayText, StringComparison.OrdinalIgnoreCase) ||
                        cur.Contains(displayText, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* try expand path */ }
            }
        }
        catch { /* ignore */ }

        try
        {
            SetFocus(combo);
            Thread.Sleep(40);
        }
        catch { /* ignore */ }

        ExpandCollapsePattern? expand = null;
        try
        {
            if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expObj) && expObj is ExpandCollapsePattern exp)
            {
                expand = exp;
                if (exp.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                    exp.Expand();
            }
        }
        catch { /* ignore */ }

        Thread.Sleep(160);

        AutomationElement? item = FindListItemMatchingDisplayText(combo, displayText, TreeScope.Descendants);
        if (item == null && searchWithin != null)
            item = FindListItemMatchingDisplayText(searchWithin, displayText, TreeScope.Descendants);
        if (item == null)
            item = FindListItemMatchingDisplayText(AutomationElement.RootElement, displayText, TreeScope.Descendants);

        if (item == null)
        {
            try
            {
                if (expand?.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                    expand.Collapse();
            }
            catch { /* ignore */ }
            return false;
        }

        try
        {
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sipObj) && sipObj is SelectionItemPattern sip)
            {
                sip.Select();
                Thread.Sleep(50);
                return true;
            }
        }
        catch { /* fall through to click */ }

        try
        {
            NativeInput.LeftClickCenter(item);
            Thread.Sleep(50);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? FindListItemMatchingDisplayText(AutomationElement root, string displayText, TreeScope scope)
    {
        AutomationElementCollection? all = null;
        try
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
            all = root.FindAll(scope, cond);
        }
        catch
        {
            return null;
        }

        AutomationElement? containsMatch = null;
        foreach (AutomationElement el in all)
        {
            try
            {
                var n = (el.Current.Name ?? "").Trim();
                if (string.Equals(n, displayText, StringComparison.OrdinalIgnoreCase))
                    return el;
                if (containsMatch == null && n.Contains(displayText, StringComparison.OrdinalIgnoreCase))
                    containsMatch = el;
            }
            catch { /* ignore */ }
        }

        return containsMatch;
    }

    private bool TryFindByTypeAndText(AutomationElement root, string typeText, string targetText, out AutomationElement? target)
    {
        target = null;
        if (!TryMapControlType(typeText, out var ct))
            return false;

        if (string.IsNullOrWhiteSpace(targetText))
        {
            target = FindChild(root, ct);
            return target != null;
        }

        target = FindDescendantByNameContains(root, ct, targetText);
        return target != null;
    }

    private bool TryMapControlType(string? typeText, out ControlType controlType)
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

    private static AutomationElement? FindScrollableAncestor(AutomationElement from)
    {
        AutomationElement? cur = from;
        while (cur != null)
        {
            try
            {
                if (cur.TryGetCurrentPattern(ScrollPattern.Pattern, out _))
                    return cur;
            }
            catch { /* ignore */ }
            try
            {
                cur = TreeWalker.ControlViewWalker.GetParent(cur);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}
