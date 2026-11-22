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
    private IntPtr _hwnd;
    private bool _embedded;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    /// <summary>由 App 注入依赖与回调。</summary>
    public void Configure(IReadOnlyList<(string header, Action click)> menuItems,
        Action onClose, Action onWake)
    {
        _onClose = onClose;
        _onWake = onWake;

        MenuRoot.Items.Clear();
        foreach (var (header, click) in menuItems)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => click();
            MenuRoot.Items.Add(mi);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
    }

    /// <summary>投屏启动后调用：把 scrcpy 窗口嵌进内容区。失败返回 false 让上层降级。</summary>
    public bool TryEmbed(IntPtr scrcpyHwnd)
    {
        if (scrcpyHwnd == IntPtr.Zero) return false;
        ContentArea.UpdateLayout();
        var w = (int)ContentArea.ActualWidth;
        var h = (int)ContentArea.ActualHeight;
        if (w < 100 || h < 100) return false;

        _embedded = _embed.Attach(scrcpyHwnd, _hwnd, w, h);
        if (!_embedded) return false;

        ContentArea.SizeChanged += (_, _) =>
        {
            if (_embedded)
                _embed.Resize((int)ContentArea.ActualWidth, (int)ContentArea.ActualHeight);
        };
        return true;
    }

    public void Detach() { _embed.Detach(); _embedded = false; }

    public void ShowSleepOverlay(bool show)
        => SleepOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

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
            _embed.Resize((int)ContentArea.ActualWidth, (int)ContentArea.ActualHeight);
    }
}
