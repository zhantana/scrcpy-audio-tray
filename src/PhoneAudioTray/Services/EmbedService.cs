using System.Runtime.InteropServices;
using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 把 scrcpy 的 SDL 窗口嵌入到 WPF 内容区。
/// 跨进程嵌入属非官方用法，失败时返回 false，由上层降级到原生窗口。
/// </summary>
public sealed class EmbedService
{
    private IntPtr _child = IntPtr.Zero;
    private long _originalStyle;
    private bool _embedded;
    private int _offsetX;
    private int _offsetY;

    public bool IsEmbedded => _embedded && _child != IntPtr.Zero && NativeMethods.IsWindow(_child);
    public IntPtr ChildHandle => _child;

    /// <summary>
    /// 把 scrcpy 的窗口（由进程 MainWindowHandle 传入）嵌入。
    /// offsetX/offsetY 是子窗口在父窗口客户区中的原点（物理像素），
    /// 用于跳过主窗口顶栏，避免画面盖住菜单栏。
    /// </summary>
    public bool Attach(IntPtr hwnd, IntPtr parentHandle, int offsetX, int offsetY, int width, int height)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;
            _offsetX = offsetX;
            _offsetY = offsetY;

            // 先藏起来，避免原窗口在桌面闪一下
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);

            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            _originalStyle = style;

            // 必须连 WS_POPUP 一起去掉：SDL 建的是弹出窗口，只摘标题栏的话
            // SetParent 后它不受父窗口裁剪，会压到顶栏上，也不跟随缩放。
            var newStyle = ((style & ~NativeMethods.DROP_POPUP_CHROME) | NativeMethods.WS_CHILD) & 0xFFFFFFFFL;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(newStyle));

            NativeMethods.SetParent(hwnd, parentHandle);
            if (NativeMethods.GetParent(hwnd) != parentHandle) return false;

            Resize(width, height);

            // SDL 有时会改回自己的尺寸，读回校对，最多强设 3 次
            for (var i = 0; i < 3; i++)
            {
                NativeMethods.GetWindowRect(hwnd, out var rc);
                var gotW = rc.Right - rc.Left;
                var gotH = rc.Bottom - rc.Top;
                if (Math.Abs(gotW - width) <= 4 && Math.Abs(gotH - height) <= 4) break;
                NativeMethods.MoveWindow(hwnd, _offsetX, _offsetY, width, height, true);
                Thread.Sleep(80);
            }

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            _child = hwnd;
            _embedded = true;
            Logger.Log($"attach hwnd={hwnd.ToInt64()} want={width}x{height}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"attach failed: {ex.Message}");
            return false;
        }
    }

    public void Resize(int width, int height)
    {
        if (!IsEmbedded || width <= 0 || height <= 0) return;
        var flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        NativeMethods.SetWindowPos(_child, IntPtr.Zero, _offsetX, _offsetY, width, height, flags);
    }

    /// <summary>降级：恢复原样式并脱离父窗口，让 scrcpy 自己显示。</summary>
    public void Detach()
    {
        try
        {
            if (_child != IntPtr.Zero && NativeMethods.IsWindow(_child))
            {
                NativeMethods.SetParent(_child, IntPtr.Zero);
                NativeMethods.SetWindowLongPtr(_child, NativeMethods.GWL_STYLE, new IntPtr(_originalStyle));
                NativeMethods.SetWindowPos(_child, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
                NativeMethods.ShowWindow(_child, NativeMethods.SW_SHOW);
            }
        }
        catch { }
        _child = IntPtr.Zero;
        _embedded = false;
    }
}
