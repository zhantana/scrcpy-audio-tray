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
                var (cw, ch) = ContentPixelSize();
                _embed.Resize(cw, ch);
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

    public void SetStatus(string text) => StatusText.Text = text;
    public void SetDeviceTitle(string text) => DeviceTitle.Text = text;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
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

    /// <summary>投屏启动后调用：把 scrcpy 窗口嵌进内容区。失败返回 false 让上层降级。</summary>
    public bool TryEmbed(IntPtr scrcpyHwnd)
    {
        if (scrcpyHwnd == IntPtr.Zero) return false;
        var (w, h) = ContentPixelSize();
        if (w < 100 || h < 100) return false;

        // 画面子窗口挂在整窗上，原点是窗口客户区左上角；顶栏占据顶部，
        // 因此把画面下移一个顶栏高度（换算成物理像素），顶栏才不会被盖住。
        var s = DpiScale;
        var offsetY = (int)Math.Round(TopBar.ActualHeight * s);
        _embedded = _embed.Attach(scrcpyHwnd, _hwnd, 0, offsetY, w, h);
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
            var (w, h) = ContentPixelSize();
            _embed.Resize(w, h);
        }
        // 窗口拖动结束后记忆尺寸（DIP），供横竖屏切换按方向恢复
        if (sizeInfo.WidthChanged || sizeInfo.HeightChanged)
            _onSizeCommitted?.Invoke((int)Math.Round(ActualWidth), (int)Math.Round(ActualHeight));
    }
}
