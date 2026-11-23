using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Services;

namespace PhoneAudioTray.UI.Views;

public partial class MainWindow : Window
{
    private readonly EmbedService _embed = new();
    private Action? _onClose;
    private Action? _onWake;
    private Action<int, int>? _onSizeCommitted;
    private IntPtr _hwnd;
    private EdgeResizeHook? _edgeHook;
    private int _dragHit; // 非 0 表示手动边缘拖拽进行中（HT 命中码）
    private NativeMethods.POINT _dragAnchor;
    private NativeMethods.RECT _dragStartRect;
    private bool _embedded;
    private bool _fullScreen;
    private WindowState _savedState = WindowState.Normal;

    /// <summary>WPF 使用 DIP，Win32/SDL 使用物理像素；返回缩放比。</summary>
    private double DpiScale
    {
        get
        {
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget is not null)
                {
                    var m = src.CompositionTarget.TransformToDevice;
                    return m.M11;
                }
            }
            catch { }
            return 1.0;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        // SizeChanged 只订一次：重复嵌入不再追加 handler（旧实现每次 TryEmbed 加一个，永不退订）
        ContentArea.SizeChanged += (_, _) =>
        {
            if (_embedded)
            {
                var (x, y, w, h) = ChildRect();
                _embed.Resize(x, y, w, h);
            }
        };
    }

    /// <summary>由 App 注入回调。</summary>
    public void SetCallbacks(Action onClose, Action onWake, Action<int, int> onSizeCommitted)
    {
        _onClose = onClose;
        _onWake = onWake;
        _onSizeCommitted = onSizeCommitted;
    }

    /// <summary>顶栏五根菜单由 App 用 AppMenus 构建（与托盘菜单同一份定义）。</summary>
    public void SetMenus(IReadOnlyList<MenuItem> roots)
    {
        MenuRoot.Items.Clear();
        foreach (var root in roots) MenuRoot.Items.Add(root);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // 嵌入画面后三边热区被子窗口吞掉，用低级鼠标钩子接管（见 EdgeResizeHook）；
        // 拖拽不用系统模态循环（锚点错位会产生吸附感），改由 StartEdgeDrag 手动跟踪
        _edgeHook = new EdgeResizeHook(TryGetEdgeHit, StartEdgeDrag);
        _edgeHook.Install();
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        src.AddHook(WndProcHook);
        Closed += (_, _) => _edgeHook.Dispose();
    }

    /// <summary>按下边缘命中：记录锚点与起始矩形，捕获鼠标，进入手动拖拽。</summary>
    private void StartEdgeDrag(int hit)
    {
        if (hit == 0 || !NativeMethods.GetCursorPos(out var pt)) return;
        NativeMethods.GetWindowRect(_hwnd, out var rc);
        _dragHit = hit;
        _dragAnchor = pt;
        _dragStartRect = rc;
        NativeMethods.SetCapture(_hwnd);
    }

    /// <summary>拖拽期间父窗口持有捕获，WM_MOUSEMOVE/WM_LBUTTONUP 都送到这里。</summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_dragHit == 0) return IntPtr.Zero;
        switch (msg)
        {
            case NativeMethods.WM_MOUSEMOVE:
                if (NativeMethods.GetCursorPos(out var pt)) DragResizeTo(pt);
                NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, EdgeResizeHook.CursorFor(_dragHit)));
                handled = true;
                break;
            case NativeMethods.WM_LBUTTONUP:
                _dragHit = 0;
                NativeMethods.ReleaseCapture();
                handled = true;
                break;
            case NativeMethods.WM_CAPTURECHANGED:
                _dragHit = 0; // 捕获被外部打断时安全退出拖拽
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 按锚点增量换算新矩形（相对位移，不把边缘直接钉在光标坐标上，避免起始跳变），
    /// 受 MinWidth/MinHeight 约束后 SetWindowPos；子窗口随 SizeChanged 自动跟随。
    /// </summary>
    private void DragResizeTo(NativeMethods.POINT pt)
    {
        var rc = _dragStartRect;
        var dx = pt.X - _dragAnchor.X;
        var dy = pt.Y - _dragAnchor.Y;
        var minW = (int)Math.Ceiling(MinWidth * DpiScale);
        var minH = (int)Math.Ceiling(MinHeight * DpiScale);
        int left = rc.Left, top = rc.Top, right = rc.Right, bottom = rc.Bottom;
        switch (_dragHit)
        {
            case NativeMethods.HTLEFT:
                left = Math.Min(rc.Left + dx, right - minW);
                break;
            case NativeMethods.HTRIGHT:
                right = Math.Max(rc.Right + dx, left + minW);
                break;
            case NativeMethods.HTBOTTOM:
                bottom = Math.Max(rc.Bottom + dy, top + minH);
                break;
            case NativeMethods.HTBOTTOMLEFT:
                left = Math.Min(rc.Left + dx, right - minW);
                bottom = Math.Max(rc.Bottom + dy, top + minH);
                break;
            default: // HTBOTTOMRIGHT
                right = Math.Max(rc.Right + dx, left + minW);
                bottom = Math.Max(rc.Bottom + dy, top + minH);
                break;
        }
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, left, top, right - left, bottom - top,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 低级钩子命中判定：仅当画面已嵌入、非最大化，且指针落在左/右/下边缘
    /// 调整带（宽度和 WindowChrome.ResizeBorderThickness 一致）时接管；
    /// 顶边在顶栏（WPF）上，WindowChrome 热区正常，不与钩子竞争。
    /// 返回 null 放行消息。
    /// </summary>
    private int? TryGetEdgeHit(int msg, int x, int y)
    {
        // 拖拽进行中：移动消息必须放行（父窗口持捕获，由 WndProcHook 跟踪），钩子不得吞掉
        if (_dragHit != 0) return null;
        if (!_embedded || WindowState == WindowState.Maximized) return null;
        if (msg != NativeMethods.WM_LBUTTONDOWN && msg != NativeMethods.WM_MOUSEMOVE) return null;

        // 热区判定与拖拽锚点统一用 GetWindowRect，坐标系一致才不会有相对跳变
        NativeMethods.GetWindowRect(_hwnd, out var rc);

        if (x < rc.Left || x >= rc.Right || y < rc.Top || y >= rc.Bottom) return null;
        var b = (int)Math.Ceiling(6 * DpiScale);
        var topBarPx = (int)Math.Round(TopBar.ActualHeight * DpiScale);
        if (y < rc.Top + topBarPx) return null;

        var left = x < rc.Left + b;
        var right = x >= rc.Right - b;
        var bottom = y >= rc.Bottom - b;
        if (!left && !right && !bottom) return null;

        return (left, right, bottom) switch
        {
            (true, false, true) => NativeMethods.HTBOTTOMLEFT,
            (false, true, true) => NativeMethods.HTBOTTOMRIGHT,
            (true, _, false) => NativeMethods.HTLEFT,
            (false, true, _) => NativeMethods.HTRIGHT,
            _ => NativeMethods.HTBOTTOM,
        };
    }

    /// <summary>
    /// 按视频比例设置主窗口尺寸（DIP），供 App 在启动 scrcpy 前调用。
    /// 返回内容区的物理像素尺寸，用于 scrcpy 的 --window-width/--window-height。
    /// </summary>
    public (int w, int h) ApplyWindowSize(int winW, int winH)
    {
        Width = winW;
        Height = winH;
        UpdateLayout();
        return ContentPixelSize();
    }

    private (int w, int h) ContentPixelSize()
    {
        ContentArea.UpdateLayout();
        var s = DpiScale;
        var w = (int)Math.Round(ContentArea.ActualWidth * s);
        var h = (int)Math.Round(ContentArea.ActualHeight * s);
        return (w, h);
    }

    /// <summary>
    /// 计算 scrcpy 子窗口的摆放矩形（父窗口客户区物理像素）。
    /// 画面全幅铺满内容区，不留调整热区内缩黑边；
    /// 边缘拉伸由 EdgeResizeHook 接管（子 HWND 会吞掉 WM_NCHITTEST，WindowChrome 热区失效）。
    /// </summary>
    private (int x, int y, int w, int h) ChildRect()
    {
        var y = (int)Math.Round(TopBar.ActualHeight * DpiScale);
        var (w, h) = ContentPixelSize();
        return (0, y, w, h);
    }

    /// <summary>投屏启动后调用：把 scrcpy 窗口嵌进内容区。失败返回 false 让上层降级。</summary>
    public bool TryEmbed(IntPtr scrcpyHwnd)
    {
        if (scrcpyHwnd == IntPtr.Zero) return false;
        var (x, y, w, h) = ChildRect();
        if (w < 100 || h < 100) return false;

        // 画面子窗口挂在整窗上，原点是窗口客户区左上角；顶栏占据顶部，
        // 因此把画面下移一个顶栏高度（换算成物理像素），顶栏才不会被盖住。
        _embedded = _embed.Attach(scrcpyHwnd, _hwnd, x, y, w, h);
        return _embedded;
    }

    public void Detach() { _embed.Detach(); _embedded = false; }

    public void ShowSleepOverlay(bool show)
        => SleepOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    public void ToggleFullscreen()
    {
        if (!_fullScreen)
        {
            _fullScreen = true;
            _savedState = WindowState;
            Topmost = true;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            _fullScreen = false;
            Topmost = false; // 置顶恢复交给 App 按设置处理
            WindowState = _savedState;
            ResizeMode = ResizeMode.CanResizeWithGrip;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _fullScreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // 双击顶栏：最大化 / 还原
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
        {
            try { DragMove(); } catch { }
            e.Handled = true;
        }
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Detach();
        Hide();
        _onClose?.Invoke();
    }

    private void BtnWake_Click(object sender, RoutedEventArgs e) => _onWake?.Invoke();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_embedded)
        {
            var (x, y, w, h) = ChildRect();
            _embed.Resize(x, y, w, h);
        }
        // 窗口拖动结束后记忆尺寸（DIP），供横竖屏切换按方向恢复
        if (sizeInfo.WidthChanged || sizeInfo.HeightChanged)
            _onSizeCommitted?.Invoke((int)Math.Round(ActualWidth), (int)Math.Round(ActualHeight));
    }
}
