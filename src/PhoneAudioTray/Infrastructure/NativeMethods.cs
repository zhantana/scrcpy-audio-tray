using System.Runtime.InteropServices;

namespace PhoneAudioTray.Infrastructure;

/// <summary>窗口嵌入所需的 Win32 调用。C# 强类型，不靠 Add-Type 拼字符串。</summary>
public static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

    // 窗口样式
    public const int GWL_STYLE = -16;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;
    public const long WS_MINIMIZEBOX = 0x00020000L;
    public const long WS_MAXIMIZEBOX = 0x00010000L;
    public const long WS_SYSMENU = 0x00080000L;
    public const long WS_CHILD = 0x40000000L;
    public const long DROP_POPUP_CHROME =
        WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;

    // SetWindowPos flags
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int count);

    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // ---- 边缘拉伸热区（WH_MOUSE_LL 钩子 + 模拟 WM_NCLBUTTONDOWN） ----

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEHOOKSTRUCT
    {
        public POINT pt;
        public IntPtr hwnd;
        public uint wHitTestCode;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_CAPTURECHANGED = 0x0215;
    public const int WM_NCLBUTTONDOWN = 0xA1;
    // 非客户区命中码：边缘拉伸方向
    public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                     HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    // 系统调整光标
    public const int IDC_SIZEWE = 32644, IDC_SIZENS = 32645, IDC_SIZENWSE = 32642, IDC_SIZENESW = 32643;


    /// <summary>
    /// 按窗口标题前缀查找 v2 自己的画面窗口（标题为 ScrcpyBridge|serial）。
    /// 必须校验窗口属于 scrcpy 进程且可见，否则任何标题碰巧匹配的第三方窗口都会被误嵌入。
    /// </summary>
    public static IntPtr FindWindowByPrefix(string prefix)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            if (!sb.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

            GetWindowThreadProcessId(h, out var pid);
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!string.Equals(p.ProcessName, "scrcpy", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }

            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
