using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace IpspoolAutomation.Services;

internal static class NativeInput
{
    internal const int SwMinimize = 6;
    /// <summary>还原最小化/最大化窗口，便于后续点击子控件。</summary>
    internal const int SwRestore = 9;

    internal const uint MouseeventfRightdown = 0x0008;
    internal const uint MouseeventfRightup = 0x0010;
    internal const uint MouseeventfLeftdown = 0x0002;
    internal const uint MouseeventfLeftup = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal static void RightClickCenter(AutomationElement element)
    {
        var r = element.Current.BoundingRectangle;
        if (r.Width <= 0 || r.Height <= 0)
            return;
        var x = (int)(r.Left + r.Width / 2);
        var y = (int)(r.Top + r.Height / 2);
        SetCursorPos(x, y);
        Thread.Sleep(40);
        mouse_event(MouseeventfRightdown, 0, 0, 0, 0);
        mouse_event(MouseeventfRightup, 0, 0, 0, 0);
    }

    internal static void LeftClickCenter(AutomationElement element)
    {
        var r = element.Current.BoundingRectangle;
        if (r.Width <= 0 || r.Height <= 0)
            return;
        var x = (int)(r.Left + r.Width / 2);
        var y = (int)(r.Top + r.Height / 2);
        SetCursorPos(x, y);
        Thread.Sleep(40);
        mouse_event(MouseeventfLeftdown, 0, 0, 0, 0);
        mouse_event(MouseeventfLeftup, 0, 0, 0, 0);
    }

    internal static void LeftClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(40);
        mouse_event(MouseeventfLeftdown, 0, 0, 0, 0);
        mouse_event(MouseeventfLeftup, 0, 0, 0, 0);
    }

    internal static void RightClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(40);
        mouse_event(MouseeventfRightdown, 0, 0, 0, 0);
        mouse_event(MouseeventfRightup, 0, 0, 0, 0);
    }
}
