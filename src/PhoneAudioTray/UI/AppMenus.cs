using System.Windows.Controls;

namespace PhoneAudioTray.UI;

/// <summary>菜单需要的主机能力。App 实现；托盘菜单与窗口顶栏菜单共用同一份定义，消除两套重复。</summary>
public interface IMenuHost
{
    bool Running { get; }
    bool ScreenOn { get; }
    bool NativeMode { get; }
    bool OnTop { get; }
    string StatusText { get; }
    string DeviceTitle { get; }
    string PauseKeyText { get; }
    string ScreenKeyText { get; }
    IReadOnlyList<(string label, string kind, Action click)> DeviceItems { get; }
    IReadOnlyList<string> SavedWifiOffline { get; }

    void StartStopForwarding();
    void ToggleScreen();
    void MediaPause();
    void WakeUnlock();
    void SelectDevice(string serial);
    void ConnectAddress(string addr);
    void RenameDevice();
    void AddDevice();
    void PairDevice();
    void AutoDiscover();
    void ClearBadDevices();
    void ForgetPin();
    void ReconnectDevice();
    void RefreshDeviceList();
    void SetWindowMode(string mode);
    void ToggleTop();
    void ResetWindowSize();
    void EditPauseKey();
    void EditScreenKey();
    void OpenSettings();
    void ShowMainWindow();
    void ToggleFullscreen();
    void ShowHotkeyHelp();
    void ShowLog();
    void About();
    void ExitApp();
}

/// <summary>
/// 菜单工厂：托盘右键菜单（扁平 + 设备子菜单）与主窗口顶栏五根菜单
/// （设备/音频/视图/帮助/关于）都从这里构建，回调与文案只有一份。
/// </summary>
public static class AppMenus
{
    private static MenuItem Item(string header, Action click)
    {
        var m = new MenuItem { Header = header };
        m.Click += (_, _) => click();
        return m;
    }

    private static MenuItem CheckItem(string header, bool isChecked, Action click, bool enabled = true)
    {
        var m = Item(header, click);
        m.IsCheckable = true;
        m.IsChecked = isChecked;
        m.IsEnabled = enabled;
        return m;
    }

    private static MenuItem Disabled(string header, string? tooltip = null)
    {
        var m = new MenuItem { Header = header, IsEnabled = false };
        if (tooltip is not null) m.ToolTip = tooltip;
        return m;
    }

    private static MenuItem Sub(string header, params object[] items)
    {
        var m = new MenuItem { Header = header };
        foreach (var i in items) m.Items.Add(i);
        return m;
    }

    /// <summary>设备菜单：USB / WiFi / 蓝牙三组 + 公共操作（spec「按连接方式分组」）。</summary>
    public static MenuItem BuildDeviceMenu(IMenuHost h)
    {
        var usb = new MenuItem { Header = "USB 有线" };
        var wifi = new MenuItem { Header = "WiFi 无线" };
        var hasUsb = false;
        var hasWifi = false;
        foreach (var (label, kind, click) in h.DeviceItems)
        {
            if (kind == "USB") { usb.Items.Add(Item(label, click)); hasUsb = true; }
            else { wifi.Items.Add(Item(label, click)); hasWifi = true; }
        }
        if (!hasUsb) usb.Items.Add(Disabled("未检测到（插线并开启 USB 调试）"));
        if (!hasWifi) wifi.Items.Add(Disabled("当前没有在线的无线设备"));

        foreach (var addr in h.SavedWifiOffline)
            wifi.Items.Add(Item($"{addr} （点此连接）", () => h.ConnectAddress(addr)));
        wifi.Items.Add(new Separator());
        wifi.Items.Add(Item("自动发现并连接", h.AutoDiscover));
        wifi.Items.Add(Item("添加设备（IP:端口）…", h.AddDevice));
        wifi.Items.Add(Item("配对新设备…", h.PairDevice));

        return Sub("设备", usb, wifi,
            Disabled("蓝牙（不可用）", "adb 不支持蓝牙传输，无法用于投屏与音频"),
            new Separator(),
            Item("重命名此设备…", h.RenameDevice),
            Item("清理无效设备", h.ClearBadDevices),
            Item("忘记解锁密码（下次重新输）", h.ForgetPin),
            Item("重新连接设备", h.ReconnectDevice),
            Item("刷新列表", h.RefreshDeviceList));
    }

    private static MenuItem BuildWindowModeMenu(IMenuHost h)
        => Sub("窗口模式",
            CheckItem("程序窗口", !h.NativeMode, () => h.SetWindowMode("embedded")),
            CheckItem("scrcpy 原生窗口", h.NativeMode, () => h.SetWindowMode("native")));

    private static MenuItem BuildHotkeyMenu(IMenuHost h)
        => Sub("快捷键",
            Item($"修改暂停键（{h.PauseKeyText}）", h.EditPauseKey),
            Item($"修改投屏键（{h.ScreenKeyText}）", h.EditScreenKey));

    /// <summary>视图菜单。纯音频或未运行时投屏相关项置灰（对齐 v1 Update-State）。</summary>
    private static MenuItem BuildViewMenu(IMenuHost h)
    {
        var screenUi = h.Running && h.ScreenOn;
        return Sub("视图",
            CheckItem("显示手机画面（投屏）", h.ScreenOn, h.ToggleScreen, enabled: h.Running),
            BuildWindowModeMenu(h),
            CheckItem("投屏窗口置顶", h.OnTop, h.ToggleTop, enabled: screenUi),
            Item("重置窗口尺寸", h.ResetWindowSize),
            new Separator(),
            Item("全屏 F11", h.ToggleFullscreen));
    }

    /// <summary>托盘右键菜单。</summary>
    public static ContextMenu BuildTrayMenu(IMenuHost h)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Disabled(h.StatusText));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(h.Running ? "停止转发" : "启动音频转发", h.StartStopForwarding));
        menu.Items.Add(Item("暂停 / 继续（手机媒体）", h.MediaPause));
        menu.Items.Add(Item("唤醒并解锁", h.WakeUnlock));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildDeviceMenu(h));
        menu.Items.Add(CheckItem("显示手机画面（投屏）", h.ScreenOn, h.ToggleScreen, enabled: h.Running));
        menu.Items.Add(BuildWindowModeMenu(h));
        menu.Items.Add(CheckItem("投屏窗口置顶", h.OnTop, h.ToggleTop, enabled: h.Running && h.ScreenOn));
        menu.Items.Add(Item("重置窗口尺寸", h.ResetWindowSize));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildHotkeyMenu(h));
        menu.Items.Add(Item("设置（画质 / 帧率 / 音频）", h.OpenSettings));
        menu.Items.Add(Item("显示 / 隐藏窗口", h.ShowMainWindow));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("退出", h.ExitApp));
        return menu;
    }

    /// <summary>主窗口顶栏五根菜单：设备 / 音频 / 视图 / 帮助 / 关于（spec「单行顶部栏」）。</summary>
    public static IReadOnlyList<MenuItem> BuildTopBarRoots(IMenuHost h)
    {
        var audio = Sub("音频",
            Item(h.Running ? "停止转发" : "启动音频转发", h.StartStopForwarding),
            Item("暂停 / 继续（手机媒体）", h.MediaPause),
            new Separator(),
            Item("唤醒并解锁", h.WakeUnlock),
            Item("设置（画质 / 帧率 / 音频）", h.OpenSettings));

        var help = Sub("帮助",
            Item("快捷键说明", h.ShowHotkeyHelp),
            Item("查看日志", h.ShowLog));

        var about = Sub("关于", Item("关于手机音频托盘", h.About));

        return new List<MenuItem> { BuildDeviceMenu(h), audio, BuildViewMenu(h), help, about };
    }
}
