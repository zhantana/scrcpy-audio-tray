using System.Runtime.InteropServices;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// WH_MOUSE_LL 低级鼠标钩子（装在本进程 UI 线程，需消息泵运转）。
/// 嵌入的 scrcpy 子 HWND 会吞掉 WM_NCHITTEST，WindowChrome 的边缘调整热区
/// 在子窗口覆盖处失效；本钩子在消息到达任何窗口之前抢先判定边缘命中，
/// 按下时回调宿主模拟 WM_NCLBUTTONDOWN 启动系统调整循环，
/// 悬停时设置调整光标并吞掉事件，避免子窗口用箭头光标覆盖。
/// </summary>
public sealed class EdgeResizeHook : IDisposable
{
    /// <summary>返回非 null 表示拦截该消息并模拟对应 HT 命中码；返回 null 放行。</summary>
    public delegate int? HitTest(int message, int x, int y);

    private readonly HitTest _hitTest;
    private readonly Action<int> _onHit;
    // 保持委托引用不被 GC（钩子 marshaling 依赖它存活）
    private readonly NativeMethods.HookProc _proc;
    private IntPtr _hook;

    public EdgeResizeHook(HitTest hitTest, Action<int> onHit)
    {
        _hitTest = hitTest;
        _onHit = onHit;
        _proc = HookProc;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc,
            NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Logger.Log($"edge resize hook install failed: win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var msg = wParam.ToInt32();
            var s = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MOUSEHOOKSTRUCT>(lParam);
            var hit = _hitTest(msg, s.pt.X, s.pt.Y);
            if (hit is int h)
            {
                if (msg == NativeMethods.WM_MOUSEMOVE)
                {
                    NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, CursorFor(h)));
                    return new IntPtr(1); // 吞掉移动，防止子窗口随后用箭头光标覆盖
                }
                // 按下：吞掉原始点击，交给 UI 线程调度器启动系统调整循环；
                // 不在钩子回调里同步 SendMessage，避免嵌套消息循环
                _onHit(h);
                return new IntPtr(1);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public static int CursorFor(int hit) => hit switch
    {
        NativeMethods.HTLEFT or NativeMethods.HTRIGHT => NativeMethods.IDC_SIZEWE,
        NativeMethods.HTTOP or NativeMethods.HTBOTTOM => NativeMethods.IDC_SIZENS,
        NativeMethods.HTTOPLEFT or NativeMethods.HTBOTTOMRIGHT => NativeMethods.IDC_SIZENWSE,
        _ => NativeMethods.IDC_SIZENESW,
    };

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
