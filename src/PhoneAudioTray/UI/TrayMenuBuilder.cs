using System.Windows.Controls;

namespace PhoneAudioTray.UI;

/// <summary>
/// 构建托盘右键菜单（WPF ContextMenu）。菜单项随状态变化重建。
/// </summary>
public static class TrayMenuBuilder
{
    public static ContextMenu Build(
        Action onToggleScreen, Action onMediaPause, Action onWake,
        Action onShowWindow, Action onResetSize, Action onExit,
        Action onEditPauseKey, Action onEditScreenKey, Action onSettings,
        Action onEmbedded, Action onNative, Action onToggleTop,
        bool running, bool native, bool onTop,
        string pauseKey, string screenKey,
        IReadOnlyList<(string label, Action click)> devices)
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItem(running ? "停止投屏" : "启动投屏", onToggleScreen));
        menu.Items.Add(MenuItem("暂停 / 继续（手机媒体）", onMediaPause));
        menu.Items.Add(MenuItem("唤醒并解锁", onWake));
        menu.Items.Add(new Separator());

        var dev = new MenuItem { Header = "设备" };
        if (devices.Count == 0) dev.Items.Add(new MenuItem { Header = "（未发现设备）", IsEnabled = false });
        foreach (var (label, click) in devices) dev.Items.Add(MenuItem(label, click));
        menu.Items.Add(dev);

        var mode = new MenuItem { Header = "窗口模式" };
        var embedded = CheckItem("程序窗口", !native, onEmbedded);
        var nativeItem = CheckItem("scrcpy 原生窗口", native, onNative);
        mode.Items.Add(embedded);
        mode.Items.Add(nativeItem);
        menu.Items.Add(mode);

        menu.Items.Add(CheckItem("投屏窗口置顶", onTop, onToggleTop));

        var keys = new MenuItem { Header = "快捷键" };
        keys.Items.Add(MenuItem($"修改暂停键（{pauseKey}）", onEditPauseKey));
        keys.Items.Add(MenuItem($"修改投屏键（{screenKey}）", onEditScreenKey));
        menu.Items.Add(keys);

        menu.Items.Add(MenuItem("设置（画质 / 帧率 / 音频）", onSettings));
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
