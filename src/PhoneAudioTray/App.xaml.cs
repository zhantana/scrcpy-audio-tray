using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Services;
using PhoneAudioTray.UI;
using PhoneAudioTray.UI.Views;
using WfMouseButtons = System.Windows.Forms.MouseButtons;

namespace PhoneAudioTray;

public partial class App : System.Windows.Application
{
    private ConfigService _config = null!;
    private AdbClient _adb = null!;
    private ScrcpyProcess _proc = null!;
    private DeviceService _devices = null!;
    private ScreenService _screen = null!;
    private MediaService _media = null!;
    private UnlockService _unlock = null!;
    private HotkeyService _hotkeys = null!;
    private TrayIconService _tray = null!;
    private MainWindow? _mainWindow;
    private DispatcherTimer? _embedTimer;
    private int _embedTicks;
    private DispatcherTimer? _sizeSaveTimer;
    private SleepMonitor? _sleepMonitor;
    // 手动唤醒后重启投屏的那一轮跳过启动前解锁，避免解两次
    private bool _skipUnlockOnce;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 在任何窗口创建前声明 DPI 感知，避免与 DPI 感知的 scrcpy 之间出现二次缩放
        try { NativeMethods.SetProcessDPIAware(); } catch { }

        if (e.Args.Contains("--selftest")) { SelfTest(); Shutdown(0); return; }

        var dir = AppContext.BaseDirectory;
        var scrcpy = AdbClient.FindScrcpy(dir);
        if (scrcpy is null)
        {
            System.Windows.MessageBox.Show("没找到 scrcpy.exe", "启动失败");
            Shutdown(1);
            return;
        }

        _config = new ConfigService();
        _config.Load();
        _adb = new AdbClient(scrcpy);
        _proc = new ScrcpyProcess(scrcpy);
        _devices = new DeviceService(_adb, _config);
        _devices.LoadNames();
        _screen = new ScreenService(_config, _adb, _proc);
        _media = new MediaService(_adb);
        _unlock = new UnlockService(_adb);
        _hotkeys = new HotkeyService(_config);
        _tray = new TrayIconService(dir);
        _sleepMonitor = new SleepMonitor(_adb);
        _sleepMonitor.AsleepChanged += (_, asleep) =>
        {
            // 程序窗口模式下在窗口内提示；原生模式下用托盘气泡
            _mainWindow?.ShowSleepOverlay(asleep);
            if (asleep && _screen.IsNative)
                _tray.Notify("手机已息屏", "按快捷键唤醒，或右键 → 唤醒并解锁");
        };

        // 事件接线
        _tray.Clicked += OnTrayClicked;
        _tray.DoubleClicked += (_, _) => ToggleScreen();
        _hotkeys.PausePressed += (_, _) => DoMediaPause();
        _hotkeys.ScreenTogglePressed += (_, _) => ToggleScreen();
        _hotkeys.RegisterFailed += (_, msg) => _tray.Notify("快捷键", msg);
        _screen.StateChanged += (_, _) => _tray.SetActive(_screen.IsRunning);

        // 自动选设备：优先沿用记忆的，其次 USB，最后任意在线设备
        var online = _devices.Refresh();
        var pick = _config.Settings.Serial;
        if (string.IsNullOrEmpty(pick) || !online.Any(d => d.Serial == pick))
        {
            var usb = online.FirstOrDefault(d => DeviceService.KindOf(d.Serial) == "USB");
            pick = (usb ?? online.FirstOrDefault())?.Serial;
        }
        _screen.Serial = pick;
        _config.Settings.Screen = true; // 启动即投屏
        _hotkeys.RegisterAll();
        if (!string.IsNullOrEmpty(pick))
            ToggleScreen(); // 启动即开始转发
        else
            _tray.Notify("没有设备", "先连手机：USB 或无线调试");
    }

    private void OnTrayClicked(object? sender, WfMouseButtons button)
    {
        if (button != WfMouseButtons.Right) return;
        var menu = BuildMenu();
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var devs = _devices.Refresh()
            .Select(d => (_devices.NameOf(d) + (DeviceService.KindOf(d.Serial) == "WiFi" ? " [WiFi]" : " [USB]"),
                          (Action)(() => SelectDevice(d.Serial))))
            .ToList();

        return TrayMenuBuilder.Build(
            onToggleScreen: ToggleScreen,
            onMediaPause: DoMediaPause,
            onWake: DoWake,
            onShowWindow: ShowMainWindow,
            onResetSize: () => { _screen.ResetSizes(); _tray.Notify("窗口尺寸", "已重置"); },
            onExit: ExitApp,
            onEditPauseKey: () => EditHotkey("暂停", true),
            onEditScreenKey: () => EditHotkey("投屏", false),
            onSettings: OpenSettings,
            onEmbedded: () => SetMode("embedded"),
            onNative: () => SetMode("native"),
            onToggleTop: () => { _screen.SetOnTop(!_config.Settings.OnTop); ApplyTopMost(); },
            running: _screen.IsRunning,
            native: _screen.IsNative,
            onTop: _config.Settings.OnTop,
            pauseKey: _config.Settings.HotkeyPause,
            screenKey: _config.Settings.HotkeyScreen,
            devices: devs);
    }

    private void SelectDevice(string serial)
    {
        _screen.Stop();
        _screen.Serial = serial;
        _tray.Notify("已切换设备", serial);
        ToggleScreen();
    }

    private void ToggleScreen()
    {
        if (_screen.IsRunning) { StopScreen(); return; }
        StartScreen();
    }

    /// <summary>停掉投屏，把屏幕控制权还给 adb（唤醒解锁的前置条件）。</summary>
    private void StopScreen()
    {
        _embedTimer?.Stop();
        _sleepMonitor?.Stop();
        _mainWindow?.ShowSleepOverlay(false);
        _mainWindow?.Detach();
        _screen.Stop();
    }

    /// <summary>启动投屏。手动唤醒后的重启通过 _skipUnlockOnce 跳过启动前解锁。</summary>
    private void StartScreen()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        var doUnlock = !_skipUnlockOnce;
        _skipUnlockOnce = false;
        if (doUnlock && !string.IsNullOrEmpty(serial))
        {
            // 启动前先解锁，避免投出锁屏画面（对齐 v1）
            var ur = UnlockWithPinPrompt(serial);
            if (!ur.Ok)
                _tray.Notify("没能自动解锁", "手机还在锁屏界面。可右键「唤醒并解锁」再试");
        }

        _config.Settings.Screen = true;
        _sleepMonitor!.Serial = _screen.Serial;
        _sleepMonitor.Start();

        if (!_screen.IsNative)
        {
            // 嵌入模式：先把窗口立起来，按视频比例定尺寸，再让 scrcpy 以内容区尺寸建在屏幕外
            EnsureMainWindow();
            _mainWindow!.Show();
            var work = System.Windows.SystemParameters.WorkArea;
            var (winW, winH) = _screen.DesiredWindowSize((int)work.Width, (int)work.Height, 40, 0);
            var (cw, ch) = _mainWindow.ApplyWindowSize(winW, winH);
            _screen.Start(cw, ch);
        }
        else
        {
            _screen.Start();
        }

        if (!_screen.IsNative)
            StartEmbedPolling();
    }

    /// <summary>轮询等待 scrcpy 窗口建好，然后嵌入。失败降级到原生窗口。</summary>
    private void StartEmbedPolling()
    {
        _embedTimer?.Stop();
        _embedTicks = 0;
        _embedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _embedTimer.Tick += (_, _) =>
        {
            _embedTicks++;
            // 按我们设定的标题前缀找 v2 自己的窗口（v1 的窗口标题是设备ID，不会误匹配）
            var prefix = $"ScrcpyBridge|{_screen.Serial}";
            var hwnd = NativeMethods.FindWindowByPrefix(prefix);
            if (hwnd != IntPtr.Zero && _mainWindow is not null && _mainWindow.TryEmbed(hwnd))
            {
                _embedTimer!.Stop();
                Logger.Log($"embedded ok hwnd={hwnd.ToInt64()}");
            }
            else if (_embedTicks > 60) // 7 秒还没嵌进去，降级
            {
                _embedTimer!.Stop();
                _tray.Notify("画面已回退到独立窗口", "嵌入失败，音频与解锁不受影响");
                Logger.Log("embed timeout -> fallback");
            }
        };
        _embedTimer.Start();
    }

    private void EnsureMainWindow()
    {
        if (_mainWindow is not null) return;
        _mainWindow = new MainWindow();
        var menuItems = BuildMenuItems();
        _mainWindow.Configure(menuItems,
            onClose: () => { _embedTimer?.Stop(); _mainWindow?.Detach(); _screen.Stop(); },
            onWake: () => DoWake(),
            onSizeCommitted: (w, h) =>
            {
                // 拖动时每一帧都会触发，防抖后只记最终尺寸
                _sizeSaveTimer?.Stop();
                _sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _sizeSaveTimer.Tick += (_, _) =>
                {
                    _sizeSaveTimer!.Stop();
                    _screen.RememberSize(w, h);
                };
                _sizeSaveTimer.Start();
            });
    }

    private List<(string header, Action click)> BuildMenuItems()
        => new()
        {
            ("启动 / 停止投屏", ToggleScreen),
            ("暂停 / 继续（手机媒体）", DoMediaPause),
            ("唤醒并解锁", DoWake),
            ($"修改暂停键（{_config.Settings.HotkeyPause}）", () => EditHotkey("暂停", true)),
            ($"修改投屏键（{_config.Settings.HotkeyScreen}）", () => EditHotkey("投屏", false)),
            ("设置（画质 / 帧率 / 音频）", OpenSettings),
            ("重置窗口尺寸", () => { _screen.ResetSizes(); _tray.Notify("窗口尺寸", "已重置"); }),
            ("退出", ExitApp),
        };

    private void OpenSettings()
    {
        var win = new SettingsWindow(_config, () =>
        {
            // 画质参数改了要重启投屏才生效
            if (_screen.IsRunning)
            {
                _embedTimer?.Stop();
                _mainWindow?.Detach();
                _screen.Stop();
                ToggleScreen();
            }
        });
        win.ShowDialog();
    }

    private void EditHotkey(string purpose, bool isPause)
    {
        _hotkeys.Suspend(); // 录当前键时不能被触发
        var current = isPause ? _config.Settings.HotkeyPause : _config.Settings.HotkeyScreen;
        var win = new HotkeyCaptureWindow(purpose, current);
        win.ShowDialog();
        var spec = win.Result;
        if (spec is null)
        {
            _hotkeys.RegisterAll(); // 取消：恢复原热键
            return;
        }
        if (isPause) _hotkeys.SetPause(spec);
        else _hotkeys.SetScreen(spec);
        _tray.Notify($"{purpose}键已设置", spec);
    }

    /// <summary>
    /// 解锁；遇到 NEED_PIN（首次没存密码）时弹一次输入框，保存后自动重试。取消则原样返回。
    /// </summary>
    private UnlockService.Result UnlockWithPinPrompt(string serial, bool forceWake = false)
    {
        var r = _unlock.Invoke(serial, forceWake);
        if (r.Message == "NEED_PIN")
        {
            var win = new PinPromptWindow();
            win.ShowDialog();
            var pin = win.Result;
            if (string.IsNullOrEmpty(pin))
                return new UnlockService.Result(false, "NEED_PIN");
            _unlock.SavePin(pin);
            r = _unlock.Invoke(serial, forceWake);
        }
        return r;
    }

    private void DoWake()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        if (string.IsNullOrEmpty(serial)) return;

        // 投屏时屏幕归 scrcpy 管，adb 点不亮：先停投屏拿回屏幕 → 强唤醒解锁 → 重启投屏
        var wasRunning = _screen.IsRunning;
        if (wasRunning)
        {
            StopScreen();
            System.Threading.Thread.Sleep(400); // 等 scrcpy 释放显示电源
        }

        var r = UnlockWithPinPrompt(serial, forceWake: wasRunning);

        if (wasRunning)
        {
            // 解锁已完成，重启投屏时跳过启动前解锁，避免解两次
            _skipUnlockOnce = true;
            StartScreen();
        }

        if (!r.Ok && r.Message == "NEED_PIN")
            _tray.Notify("需要锁屏密码", "请在托盘设置里填写，或手动解锁手机");
        else if (r.Ok)
            _tray.Notify("唤醒解锁", r.Message);
        else
            _tray.Notify("唤醒解锁失败", r.Message);
    }

    private void SetMode(string mode)
    {
        if (_screen.IsNative == (mode == "native")) return; // 没变
        var wasRunning = _screen.IsRunning;
        if (wasRunning)
        {
            _embedTimer?.Stop();
            _mainWindow?.Detach();
            _mainWindow?.Hide();
            _screen.Stop();
        }
        _screen.SetMode(mode);
        if (wasRunning) ToggleScreen();
        _tray.Notify("窗口模式", mode == "native" ? "scrcpy 原生窗口" : "程序窗口");
    }

    private void ApplyTopMost()
    {
        if (_mainWindow is not null)
            _mainWindow.Topmost = _config.Settings.OnTop;
        // 原生模式的置顶要重启投屏才生效
        if (_screen.IsNative && _screen.IsRunning)
        {
            _screen.Stop();
            ToggleScreen();
        }
    }

    private void DoMediaPause()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        var r = _media.TogglePause(serial ?? "");
        _tray.Notify("媒体暂停", r.Message);
    }

    private void ShowMainWindow()
    {
        if (_screen.IsNative) return; // 原生模式没有我们的窗口
        EnsureMainWindow();
        _mainWindow!.Show();
        _mainWindow.Activate();
    }

    private void ExitApp()
    {
        _screen.Stop();
        _sleepMonitor?.Stop();
        _hotkeys.Dispose();
        _tray.Dispose();
        _config.Save();
        Shutdown(0);
    }

    private void SelfTest()
    {
        var dir = AppContext.BaseDirectory;
        var scrcpy = AdbClient.FindScrcpy(dir);
        System.Console.WriteLine($"[selftest] scrcpy: {scrcpy ?? "未找到"}");
        if (scrcpy is null) return;
        var adb = new AdbClient(scrcpy);
        var devs = adb.ListDevices();
        System.Console.WriteLine($"[selftest] devices: {devs.Count}");
        foreach (var d in devs)
        {
            System.Console.WriteLine($"[selftest]   {d.Serial} model={d.Model} kind={DeviceService.KindOf(d.Serial)}");
            var vs = adb.GetVideoSize(d.Serial);
            System.Console.WriteLine($"[selftest]   videoSize={(vs.Length == 2 ? $"{vs[0]}x{vs[1]}" : "读不到")}");
            var ss = adb.GetScreenSize(d.Serial);
            System.Console.WriteLine($"[selftest]   screenSize={ss[0]}x{ss[1]}");
            System.Console.WriteLine($"[selftest]   mediaSession={adb.HasMediaSession(d.Serial)}");
        }
        System.Console.WriteLine("[selftest] hotkey parse Ctrl+Alt+P: " +
            HotkeyParser.TryParse("Ctrl+Alt+P", out var m, out var k) + $" mods={m} key={k}");
        System.Console.WriteLine("[selftest] hotkey parse Pause: " +
            HotkeyParser.TryParse("Pause", out var m2, out var k2) + $" mods={m2} key={k2}");
        System.Console.WriteLine("[selftest] args(native=false):");
        System.Console.WriteLine(ScrcpyProcess.GetArgsForTest(new Models.ScrcpyOptions
        {
            Serial = "TEST", Screen = true, NativeWindow = false,
            MaxFps = 60, VideoBitRate = 16000000, VideoCodec = "h265",
            WindowTitle = "ScrcpyBridge|TEST", WindowWidth = 400, WindowHeight = 880,
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _config?.Save(); } catch { }
        base.OnExit(e);
    }
}
