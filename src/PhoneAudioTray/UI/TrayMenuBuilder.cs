using System.Windows;
using System.Windows.Controls;
using PhoneAudioTray.Services;

namespace PhoneAudioTray.UI;

/// <summary>
/// 构建托盘右键菜单（WPF ContextMenu）。菜单项随状态变化重建。
/// </summary>
public static class TrayMenuBuilder
{
    public static ContextMenu Build(Action onToggleScreen, Action onMediaPause,
        Action onShowWindow, Action onResetSize, Action onExit,
        bool running, bool screenOn, bool native, bool onTop,
        IReadOnlyList<(string label, Action click)> devices)
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItem(running ? "停止投屏" : "启动投屏", onToggleScreen));
        menu.Items.Add(MenuItem("暂停 / 继续（手机媒体）", onMediaPause));
        menu.Items.Add(new Separator());

        var dev = new MenuItem { Header = "设备" };
        if (devices.Count == 0) dev.Items.Add(new MenuItem { Header = "（未发现设备）", IsEnabled = false });
        foreach (var (label, click) in devices) dev.Items.Add(MenuItem(label, click));
        menu.Items.Add(dev);

        var mode = new MenuItem { Header = "窗口模式" };
        mode.Items.Add(CheckItem("程序窗口", !native, () => { }));
        mode.Items.Add(CheckItem("scrcpy 原生窗口", native, () => { }));
        menu.Items.Add(mode);

        menu.Items.Add(CheckItem("投屏窗口置顶", onTop, () => { }));
        menu.Items.Add(MenuItem("显示 / 隐藏窗口", onShowWindow));
        menu.Items.Add(MenuItem("重置窗口尺寸", onResetSize));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuItem("退出", onExit));
        return menu;
    }

    private static MenuItem MenuItem(string header, Action click)
    {
        var m = new MenuItem { Header = header };
        m.Click += (_, _) => click();
        return m;
    }

    private static MenuItem CheckItem(string header, bool isChecked, Action click)
    {
        var m = MenuItem(header, click);
        m.IsCheckable = true;
        m.IsChecked = isChecked;
        return m;
    }
}
