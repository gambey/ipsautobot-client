using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Automation;
using IpspoolAutomation.Automation;
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

    public AutomationElement? FindDialogWindow(AutomationElement merchantRoot, string titleContains)
    {
        if (merchantRoot == null || string.IsNullOrWhiteSpace(titleContains))
            return null;
        var needle = titleContains.Trim();
        int merchantPid;
        try
        {
            merchantPid = merchantRoot.Current.ProcessId;
        }
        catch
        {
            return null;
        }

        // 1) 模态框可能在商家主窗口子树内（Window / Pane）
        var inSubtree = TryFindDialogInSubtree(merchantRoot, merchantPid, needle);
        if (inSubtree != null)
            return inSubtree;

        // 2) UIA：桌面范围 Window，同进程优先
        var byUia = FindDialogWindowByUiaDesktop(merchantPid, needle);
        if (byUia != null)
            return byUia;

        // 3) Win32 标题栏文字（部分 WPF 弹窗 UIA Name 为空，但 GetWindowText 有标题）
        return FindDialogWindowByWin32Title(merchantPid, needle);
    }

    public AutomationElement? FindLikelyMathDialog(AutomationElement merchantRoot)
    {
        if (merchantRoot == null)
            return null;
        int merchantPid;
        try
        {
            merchantPid = merchantRoot.Current.ProcessId;
        }
        catch
        {
            return null;
        }

        var candidates = new List<AutomationElement>();
        foreach (var ct in new[] { ControlType.Window, ControlType.Pane })
        {
            try
            {
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ct);
                var all = AutomationElement.RootElement.FindAll(TreeScope.Descendants, cond);
                foreach (AutomationElement el in all)
                {
                    try
                    {
                        if (el.Current.ProcessId == merchantPid)
                            candidates.Add(el);
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
        }

        var bestScore = 0;
        AutomationElement? best = null;
        foreach (var c in candidates)
        {
            var score = ScoreMathDialogCandidate(c);
            if (score <= bestScore)
                continue;
            bestScore = score;
            best = c;
        }

        return bestScore >= 4 ? best : null;
    }

    public AutomationElement? FindDialogByInnerTarget(AutomationElement merchantRoot, string targetType, string targetText)
    {
        if (merchantRoot == null || string.IsNullOrWhiteSpace(targetType))
            return null;
        int merchantPid;
        try
        {
            merchantPid = merchantRoot.Current.ProcessId;
        }
        catch
        {
            return null;
        }

        var candidates = new List<AutomationElement>();
        CollectDialogLikeCandidates(merchantRoot, candidates);
        try
        {
            foreach (var ct in new[] { ControlType.Window, ControlType.Pane })
            {
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ct);
                var all = AutomationElement.RootElement.FindAll(TreeScope.Descendants, cond);
                foreach (AutomationElement el in all)
                {
                    try
                    {
                        if (el.Current.ProcessId == merchantPid)
                            candidates.Add(el);
                    }
                    catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }

        foreach (var c in candidates)
        {
            try
            {
                if (TryFindByTypeAndText(c, targetType, targetText ?? "", out var _))
                    return c;
            }
            catch { /* ignore */ }
        }

        return null;
    }

    public string BuildDialogSearchDiagnostics(AutomationElement merchantRoot, string titleContains, int maxItems = 8)
    {
        if (merchantRoot == null)
            return "dialog诊断：merchantRoot为空。";
        var needle = (titleContains ?? "").Trim();
        int merchantPid;
        try
        {
            merchantPid = merchantRoot.Current.ProcessId;
        }
        catch
        {
            return "dialog诊断：无法读取merchantRoot进程ID。";
        }

        var lines = new List<string>
        {
            $"dialog诊断：keyword=\"{needle}\"，pid={merchantPid}。"
        };

        var sampled = 0;
        try
        {
            NativeInput.EnumWindows((hWnd, _) =>
            {
                if (sampled >= maxItems)
                    return false;
                try
                {
                    if (NativeInput.GetWindowThreadProcessId(hWnd, out var pid) == 0 || pid != (uint)merchantPid)
                        return true;
                    var visible = NativeInput.IsWindowVisible(hWnd) ? "Y" : "N";
                    var sb = new StringBuilder(256);
                    _ = NativeInput.GetWindowText(hWnd, sb, sb.Capacity);
                    var title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title))
                        title = "<empty>";
                    lines.Add($"win32[{sampled + 1}] visible={visible} title=\"{title}\"");
                    sampled++;
                }
                catch { /* ignore */ }
                return true;
            }, IntPtr.Zero);
        }
        catch { /* ignore */ }

        if (sampled == 0)
            lines.Add("win32候选：0。");
        return string.Join(" | ", lines);
    }

    private static AutomationElement? TryFindDialogInSubtree(AutomationElement merchantRoot, int merchantPid, string needle)
    {
        foreach (var ct in new[] { ControlType.Window, ControlType.Pane })
        {
            AutomationElementCollection? all = null;
            try
            {
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ct);
                all = merchantRoot.FindAll(TreeScope.Descendants, cond);
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement el in all)
            {
                try
                {
                    if (el.Current.ProcessId != merchantPid)
                        continue;
                    var name = el.Current.Name ?? "";
                    if (name.Contains(needle, StringComparison.Ordinal))
                        return el;
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        return null;
    }

    private static AutomationElement? FindDialogWindowByUiaDesktop(int merchantPid, string needle)
    {
        AutomationElementCollection? all = null;
        try
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
            all = AutomationElement.RootElement.FindAll(TreeScope.Descendants, cond);
        }
        catch
        {
            return null;
        }

        AutomationElement? anyMatch = null;
        foreach (AutomationElement el in all)
        {
            try
            {
                if (el.Current.ProcessId != merchantPid)
                    continue;
                var name = el.Current.Name ?? "";
                if (name.Contains(needle, StringComparison.Ordinal))
                    return el;
            }
            catch
            {
                /* ignore */
            }
        }

        foreach (AutomationElement el in all)
        {
            try
            {
                var name = el.Current.Name ?? "";
                if (!name.Contains(needle, StringComparison.Ordinal))
                    continue;
                anyMatch ??= el;
            }
            catch
            {
                /* ignore */
            }
        }

        return anyMatch;
    }

    private static AutomationElement? FindDialogWindowByWin32Title(int merchantPid, string needle)
    {
        AutomationElement? found = null;
        var sb = new StringBuilder(512);
        NativeInput.EnumWindows((hWnd, _) =>
        {
            if (found != null)
                return false;
            try
            {
                if (!NativeInput.IsWindowVisible(hWnd))
                    return true;
                if (NativeInput.GetWindowThreadProcessId(hWnd, out var pid) == 0 || pid != (uint)merchantPid)
                    return true;
                sb.Clear();
                if (NativeInput.GetWindowText(hWnd, sb, sb.Capacity) <= 0)
                    return true;
                var title = sb.ToString();
                if (!title.Contains(needle, StringComparison.Ordinal))
                    return true;
                found = AutomationElement.FromHandle(hWnd);
            }
            catch
            {
                /* ignore */
            }

            return found == null;
        }, IntPtr.Zero);

        if (found != null)
            return found;

        // 少数场景弹窗不在同进程，兜底按标题全局查找可见窗口。
        NativeInput.EnumWindows((hWnd, _) =>
        {
            if (found != null)
                return false;
            try
            {
                if (!NativeInput.IsWindowVisible(hWnd))
                    return true;
                sb.Clear();
                if (NativeInput.GetWindowText(hWnd, sb, sb.Capacity) <= 0)
                    return true;
                var title = sb.ToString();
                if (!title.Contains(needle, StringComparison.Ordinal))
                    return true;
                found = AutomationElement.FromHandle(hWnd);
            }
            catch
            {
                /* ignore */
            }

            return found == null;
        }, IntPtr.Zero);

        return found;
    }

    private static int ScoreMathDialogCandidate(AutomationElement root)
    {
        if (root == null)
            return 0;
        var score = 0;
        try
        {
            var title = root.Current.Name ?? "";
            if (title.Contains("反作弊", StringComparison.Ordinal) || title.Contains("提醒", StringComparison.Ordinal))
                score += 3;
        }
        catch { /* ignore */ }

        try
        {
            if (CountByType(root, ControlType.Edit) >= 1)
                score += 2;
            if (CountByType(root, ControlType.Button) >= 2)
                score += 2;
            if (ContainsMathExpressionText(root))
                score += 3;
        }
        catch { /* ignore */ }

        return score;
    }

    private static int CountByType(AutomationElement root, ControlType ct)
    {
        try
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ct);
            return root.FindAll(TreeScope.Descendants, cond).Count;
        }
        catch
        {
            return 0;
        }
    }

    private static bool ContainsMathExpressionText(AutomationElement root)
    {
        try
        {
            var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement el in all)
            {
                try
                {
                    var n = el.Current.Name ?? "";
                    if (n.Contains("+", StringComparison.Ordinal) || n.Contains("＋", StringComparison.Ordinal) ||
                        n.Contains("-", StringComparison.Ordinal) || n.Contains("－", StringComparison.Ordinal))
                        return true;
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }

        return false;
    }

    private static void CollectDialogLikeCandidates(AutomationElement root, List<AutomationElement> output)
    {
        if (root == null)
            return;
        foreach (var ct in new[] { ControlType.Window, ControlType.Pane })
        {
            try
            {
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ct);
                var all = root.FindAll(TreeScope.Descendants, cond);
                foreach (AutomationElement el in all)
                    output.Add(el);
            }
            catch { /* ignore */ }
        }
    }

    public AutomationElement? FindFirstEditInSubtree(AutomationElement root)
    {
        if (root == null)
            return null;
        try
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            var all = root.FindAll(TreeScope.Descendants, cond);
            foreach (AutomationElement el in all)
                return el;
        }
        catch
        {
            return null;
        }

        return null;
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

    public bool TryRestoreWindowNormal(AutomationElement windowElement)
    {
        if (windowElement == null)
            return false;
        try
        {
            if (windowElement.TryGetCurrentPattern(WindowPattern.Pattern, out var wpObj) && wpObj is WindowPattern wp)
            {
                wp.SetWindowVisualState(WindowVisualState.Normal);
                Thread.Sleep(80);
            }
        }
        catch { /* ignore */ }

        try
        {
            var hwnd = new IntPtr(windowElement.Current.NativeWindowHandle);
            if (hwnd == IntPtr.Zero)
                return false;
            _ = NativeInput.ShowWindow(hwnd, NativeInput.SwRestore);
            Thread.Sleep(120);
            return true;
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

    /// <summary>
    /// 行在滚动区内有足够高度露出（避免仅顶部一条缝或中心落在横向滚动条上），才适合右键菜单。
    /// </summary>
    private static bool IsDataItemAdequatelyVisibleForRowClick(AutomationElement element)
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

        System.Windows.Rect rowRect;
        AutomationElement? container;
        try
        {
            rowRect = element.Current.BoundingRectangle;
            container = FindScrollableAncestor(element);
        }
        catch
        {
            return false;
        }
        if (rowRect.Width <= 0 || rowRect.Height <= 0)
            return false;

        if (container == null)
            return true;

        System.Windows.Rect viewport;
        try
        {
            viewport = container.Current.BoundingRectangle;
        }
        catch
        {
            return true;
        }

        var bottomReserve = GetViewportBottomReserveForRowClicks(container);
        var effBottom = viewport.Bottom - bottomReserve;
        if (effBottom <= viewport.Top + 1)
            return false;

        var il = Math.Max(rowRect.Left, viewport.Left);
        var it = Math.Max(rowRect.Top, viewport.Top);
        var ir = Math.Min(rowRect.Right, viewport.Right);
        var ib = Math.Min(rowRect.Bottom, effBottom);
        var visibleH = ib - it;
        if (visibleH <= 0 || ir <= il)
            return false;

        var needH = Math.Max(18.0, Math.Min(rowRect.Height * 0.65, rowRect.Height - 1));
        return visibleH >= needH;
    }

    /// <summary>横向滚动条会占用宿主底部像素，中心点仍可能在 UIA 视口内但实际点在条带上。</summary>
    private static double GetViewportBottomReserveForRowClicks(AutomationElement scrollContainer)
    {
        try
        {
            if (scrollContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj) && spObj is ScrollPattern sp)
            {
                if (sp.Current.HorizontallyScrollable)
                    return 28;
            }
        }
        catch { /* ignore */ }
        return 10;
    }

    public bool TryGetGridRowContextClickPoint(AutomationElement row, out int x, out int y)
    {
        x = y = 0;
        if (row == null)
            return false;
        System.Windows.Rect rowRect;
        AutomationElement? container;
        try
        {
            rowRect = row.Current.BoundingRectangle;
            container = FindScrollableAncestor(row);
        }
        catch
        {
            return false;
        }
        if (rowRect.Width <= 0 || rowRect.Height <= 0)
            return false;

        if (container == null)
        {
            x = (int)(rowRect.Left + rowRect.Width / 2);
            y = (int)(rowRect.Top + rowRect.Height / 2);
            return true;
        }

        System.Windows.Rect viewport;
        try
        {
            viewport = container.Current.BoundingRectangle;
        }
        catch
        {
            x = (int)(rowRect.Left + rowRect.Width / 2);
            y = (int)(rowRect.Top + rowRect.Height / 2);
            return true;
        }

        var bottomReserve = GetViewportBottomReserveForRowClicks(container);
        var effBottom = viewport.Bottom - bottomReserve;
        if (effBottom <= viewport.Top + 1)
            return false;

        var il = Math.Max(rowRect.Left, viewport.Left);
        var it = Math.Max(rowRect.Top, viewport.Top);
        var ir = Math.Min(rowRect.Right, viewport.Right);
        var ib = Math.Min(rowRect.Bottom, effBottom);
        if (ir <= il || ib <= it)
            return false;

        x = (int)((il + ir) / 2);
        y = (int)((it + ib) / 2);
        return true;
    }

    public void LeftClickGridRowForContextMenu(AutomationElement row)
    {
        if (TryGetGridRowContextClickPoint(row, out var px, out var py))
            NativeInput.LeftClickAt(px, py);
        else
            NativeInput.LeftClickCenter(row);
    }

    public void RightClickGridRowForContextMenu(AutomationElement row)
    {
        if (TryGetGridRowContextClickPoint(row, out var px, out var py))
            NativeInput.RightClickAt(px, py);
        else
            NativeInput.RightClickCenter(row);
    }

    public bool TryEnsureGridRowReadyForContextMenu(AutomationElement dataItem, int maxSteps = 28)
    {
        if (dataItem == null)
            return false;

        try
        {
            if (dataItem.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var siObj) && siObj is ScrollItemPattern sip)
            {
                sip.ScrollIntoView();
                Thread.Sleep(90);
            }
        }
        catch { /* ignore */ }

        if (IsDataItemAdequatelyVisibleForRowClick(dataItem))
            return true;

        // 不能用 TryEnsureDataItemVisible：其「中心点在视口内」过宽，会跳过滚动，留下被横向滚动条压住的行。
        var scrollContainer = FindScrollableAncestor(dataItem);
        if (scrollContainer == null)
            return IsDataItemAdequatelyVisibleForRowClick(dataItem);
        if (!scrollContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj) || spObj is not ScrollPattern sp)
            return IsDataItemAdequatelyVisibleForRowClick(dataItem);
        if (!sp.Current.VerticallyScrollable)
            return IsDataItemAdequatelyVisibleForRowClick(dataItem);

        for (var i = 0; i < maxSteps; i++)
        {
            if (IsDataItemAdequatelyVisibleForRowClick(dataItem))
                return true;
            try
            {
                var rowRect = dataItem.Current.BoundingRectangle;
                var viewport = scrollContainer.Current.BoundingRectangle;
                var effMidY = viewport.Top + (viewport.Bottom - GetViewportBottomReserveForRowClicks(scrollContainer) - viewport.Top) / 2;
                var rowMidY = rowRect.Top + rowRect.Height / 2;
                var amount = rowMidY < effMidY ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement;
                sp.ScrollVertical(amount);
            }
            catch
            {
                break;
            }
            Thread.Sleep(45);
        }

        return IsDataItemAdequatelyVisibleForRowClick(dataItem);
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

    public void ToggleOrClickCheckbox(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var tpObj) && tpObj is TogglePattern tp)
            {
                tp.Toggle();
                return;
            }
        }
        catch
        {
            // fall through to click
        }

        LeftClickElement(element);
    }

    public bool TrySetSelectionState(AutomationElement element, bool selected, out string? failureReason)
    {
        failureReason = null;
        if (element == null)
        {
            failureReason = "目标元素为空。";
            return false;
        }

        try
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var tObj) && tObj is TogglePattern tp)
                return TrySetStateViaTogglePattern(tp, selected, out failureReason);

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sObj) && sObj is SelectionItemPattern sip)
            {
                try
                {
                    if (selected)
                        sip.Select();
                    else
                        sip.RemoveFromSelection();
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    if (selected)
                    {
                        LeftClickElement(element);
                        return true;
                    }

                    return false;
                }
            }

            if (selected)
            {
                LeftClickElement(element);
                return true;
            }

            failureReason = "不支持取消选中：控件无 TogglePattern 或 SelectionItemPattern。";
            return false;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private static bool TrySetStateViaTogglePattern(TogglePattern tp, bool selected, out string? failureReason)
    {
        failureReason = null;
        for (var i = 0; i < 6; i++)
        {
            ToggleState s;
            try
            {
                s = tp.Current.ToggleState;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }

            if (selected)
            {
                if (s == ToggleState.On)
                    return true;
            }
            else if (s == ToggleState.Off)
            {
                return true;
            }

            try
            {
                tp.Toggle();
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        failureReason = "TogglePattern：多次切换后仍未达到目标选中状态。";
        return false;
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
        if (string.Equals(typeText, "checkbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeText, "勾选框", StringComparison.Ordinal))
        {
            controlType = ControlType.CheckBox;
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

    /// <inheritdoc />
    public bool TryJumpOrderMarketToPage(AutomationElement merchantRoot, int pageOneBased)
    {
        if (pageOneBased < 1 || pageOneBased > 9)
            return false;
        var pageStr = pageOneBased.ToString(CultureInfo.InvariantCulture);

        var anchor = FindDescendantNameContainsForJumpLabel(merchantRoot);
        var input = anchor != null
            ? FindPageJumpInputRightOfLabel(merchantRoot, anchor)
            : null;
        input ??= FindPageJumpComboBelowGridHeuristic(merchantRoot);

        if (input == null)
            return false;

        var valueOk = false;
        try
        {
            var ct = input.Current.ControlType;
            if (ct == ControlType.ComboBox)
                valueOk = TrySelectComboBoxByDisplayText(input, pageStr, merchantRoot);
            else if (ct == ControlType.Edit)
            {
                SetEditValue(input, pageStr);
                valueOk = true;
            }
        }
        catch
        {
            return false;
        }

        if (!valueOk)
            return false;

        Thread.Sleep(120);
        var okBtn = FindConfirmButtonRightOfInput(merchantRoot, input);
        if (okBtn == null)
            return false;
        try
        {
            InvokeButton(okBtn);
        }
        catch
        {
            return false;
        }

        Thread.Sleep(400);
        return true;
    }

    private static AutomationElement? FindDescendantNameContainsForJumpLabel(AutomationElement root)
    {
        foreach (var ct in new[] { ControlType.Text, ControlType.Group, ControlType.Pane, ControlType.Custom })
        {
            AutomationElementCollection all;
            try
            {
                all = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement el in all)
            {
                try
                {
                    var n = el.Current.Name ?? "";
                    if (n.Contains("跳转到页面", StringComparison.Ordinal) ||
                        n.Contains("跳转到页", StringComparison.Ordinal))
                        return el;
                }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    private static AutomationElement? FindPageJumpInputRightOfLabel(AutomationElement merchantRoot, AutomationElement anchor)
    {
        System.Windows.Rect la;
        try
        {
            la = anchor.Current.BoundingRectangle;
            if (la.Width <= 0 && la.Height <= 0)
                return null;
        }
        catch
        {
            return null;
        }

        var laRight = la.Right;
        var laMidY = la.Top + la.Height / 2;

        AutomationElement? Pick(ControlType t)
        {
            AutomationElement? best = null;
            var bestScore = double.MaxValue;
            AutomationElementCollection all;
            try
            {
                all = merchantRoot.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, t));
            }
            catch
            {
                return null;
            }

            foreach (AutomationElement c in all)
            {
                try
                {
                    var r = c.Current.BoundingRectangle;
                    if (r.Width <= 0 || r.Height <= 0)
                        continue;
                    if (r.Left < laRight - 12)
                        continue;
                    var cy = r.Top + r.Height / 2;
                    if (Math.Abs(cy - laMidY) > 88)
                        continue;
                    var score = r.Left + Math.Abs(cy - laMidY) * 0.6;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                catch { /* ignore */ }
            }

            return best;
        }

        return Pick(ControlType.ComboBox) ?? Pick(ControlType.Edit);
    }

    private AutomationElement? FindPageJumpComboBelowGridHeuristic(AutomationElement merchantRoot)
    {
        System.Windows.Rect win;
        try
        {
            win = merchantRoot.Current.BoundingRectangle;
        }
        catch
        {
            return null;
        }

        double stripTop;
        try
        {
            var grid = HelperGridReader.FindMainGrid(merchantRoot);
            if (grid != null)
            {
                var gr = grid.Current.BoundingRectangle;
                stripTop = gr.Bottom - 24;
            }
            else
                stripTop = win.Top + win.Height * 0.55;
        }
        catch
        {
            stripTop = win.Top + win.Height * 0.55;
        }

        AutomationElement? best = null;
        double bestTop = double.MinValue;
        try
        {
            var all = merchantRoot.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
            foreach (AutomationElement c in all)
            {
                try
                {
                    var r = c.Current.BoundingRectangle;
                    if (r.Width <= 0 || r.Width > 260)
                        continue;
                    if (r.Top + r.Height < stripTop)
                        continue;
                    if (r.Top >= bestTop)
                    {
                        bestTop = r.Top;
                        best = c;
                    }
                }
                catch { /* ignore */ }
            }
        }
        catch
        {
            return null;
        }

        return best;
    }

    private static AutomationElement? FindConfirmButtonRightOfInput(AutomationElement merchantRoot, AutomationElement input)
    {
        System.Windows.Rect ir;
        try
        {
            ir = input.Current.BoundingRectangle;
        }
        catch
        {
            return null;
        }

        var irMidY = ir.Top + ir.Height / 2;
        AutomationElement? best = null;
        var bestScore = double.MaxValue;
        try
        {
            var all = merchantRoot.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement b in all)
            {
                try
                {
                    var n = (b.Current.Name ?? "").Trim();
                    if (n != "确定")
                        continue;
                    var br = b.Current.BoundingRectangle;
                    if (br.Width <= 0)
                        continue;
                    if (br.Left < ir.Right - 50)
                        continue;
                    var bc = br.Top + br.Height / 2;
                    if (Math.Abs(bc - irMidY) > 72)
                        continue;
                    var score = br.Left - ir.Right + Math.Abs(bc - irMidY) * 0.35;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = b;
                    }
                }
                catch { /* ignore */ }
            }
        }
        catch
        {
            return null;
        }

        if (best != null)
            return best;

        try
        {
            var win = merchantRoot.Current.BoundingRectangle;
            var yMin = win.Top + win.Height * 0.58;
            var all = merchantRoot.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement b in all)
            {
                try
                {
                    if ((b.Current.Name ?? "").Trim() != "确定")
                        continue;
                    var br = b.Current.BoundingRectangle;
                    if (br.Top + br.Height / 2 < yMin)
                        continue;
                    return b;
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }

        return null;
    }
}
