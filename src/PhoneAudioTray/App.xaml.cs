using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Models;
using PhoneAudioTray.Services;
using PhoneAudioTray.UI;
using PhoneAudioTray.UI.Views;
using WfMouseButtons = System.Windows.Forms.MouseButtons;

namespace PhoneAudioTray;

public partial class App : System.Windows.Application, IMenuHost
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
    private KeepAwake _keepAwake = null!;
    private DevicePoller _poller = null!;
    private MainWindow? _mainWindow;

    private Mutex? _mutex;
    private readonly SemaphoreSlim _gate = new(1, 1); // 启停串行化：防自动重连与手动操作并发
    private DispatcherTimer? _embedTimer;
    private int _embedTicks;
    private DispatcherTimer? _sizeSaveTimer;
    private bool _userStopped;    // 手动停止后，自动重连不复活（对齐 v1）
    private string? _manualPick;  // 手动选过的设备优先于 USB 优先默认规则（会话内有效）
    private DateTime _lastSwitch = DateTime.MinValue;
    private List<AdbClient.DeviceInfo> _deviceCache = new();

    /// <summary>顶栏 28（DIP），窗口高度中不属于画面的部分。</summary>
    private const int ChromeH = 28;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 在任何窗口创建前声明 DPI 感知，避免与 DPI 感知的 scrcpy 之间出现二次缩放
        try { NativeMethods.SetProcessDPIAware(); } catch { }

        _mutex = TryCreateMutex();
        if (_mutex is null)
        {
            System.Windows.MessageBox.Show("手机音频托盘已经在运行了", "单实例");
            Shutdown(0);
            return;
        }

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
        _keepAwake = new KeepAwake(_adb);
        _poller = new DevicePoller(_adb, Dispatcher);

        RegisterExceptionHandlers();
        WireEvents();
        _hotkeys.RegisterAll();

        if (_config.LoadCorrupt)
            _tray.Notify("配置文件损坏", "settings.json 无法读取，已按默认设置启动；原文件已留档");
        if (_tray.IconFallback)
            _tray.Notify("托盘图标缺失", "Resources 下图标文件没找到，运行状态无法从图标区分");

        // 启动即自动转发（spec「启动行为」）；设备未就绪时后台重试，全程不弹对话框
        _ = RunStartupAsync();
    }

    private static Mutex? TryCreateMutex()
    {
        foreach (var name in new[] { @"Global\ScrcpyAudioTrayV2", @"Local\ScrcpyAudioTrayV2" })
        {
            try
            {
                var m = new Mutex(true, name, out var created);
                if (created) return m;
                m.Dispose();
            }
            catch { }
        }
        return null;
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Logger.Log($"UI 异常: {e.Exception}");
            _tray.Notify("程序异常", e.Exception.Message);
            e.Handled = true; // 非致命异常不崩进程
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Log($"未处理异常: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Log($"后台任务异常: {e.Exception}");
            e.SetObserved();
        };
    }

    private void WireEvents()
    {
        _tray.Clicked += OnTrayClicked;
        _tray.DoubleClicked += (_, _) => _ = OnTrayDoubleClicked();
        _hotkeys.PausePressed += (_, _) => DoMediaPause();
        _hotkeys.ScreenTogglePressed += (_, _) => _ = ToggleScreenAsync();
        _hotkeys.RegisterFailed += (_, msg) => _tray.Notify("快捷键", msg);

        // proc.Exited 从线程池线程触发（Process.CompletionCallback），而 UpdateStatus
        // 会重建 WPF 菜单/托盘图标，必须在 STA UI 线程执行，故统一封送
        _screen.StateChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!_screen.IsRunning) _poller.Stop();
            UpdateStatus();
        });

        _screen.VideoSizeDetected += (w, h) => Dispatcher.BeginInvoke(() => OnVideoTexture(w, h));

        _poller.AsleepChanged += (_, asleep) =>
        {
            _mainWindow?.ShowSleepOverlay(asleep);
            if (asleep && !_config.Settings.Screen) return; // 纯音频没有画面，不做息屏提示
            if (asleep && _screen.IsNative)
                _tray.Notify("手机已息屏", "按快捷键唤醒，或右键 → 唤醒并解锁");
        };
        _poller.OrientationChanged += (_, ratio) =>
        {
            if (!_screen.IsRunning || _screen.IsNative || _mainWindow is null) return;
            var work = SystemParameters.WorkArea;
            var (winW, winH) = _screen.DesiredWindowSize((int)work.Width, (int)work.Height, ChromeH, 0, ratio);
            _mainWindow.ApplyWindowSize(winW, winH); // 内容区尺寸变化 → 嵌入子窗口自动跟随
        };
        _poller.DevicesRefreshed += (_, devs) =>
        {
            _deviceCache = devs.ToList();
            TransportCheck();
        };
        _poller.SoundStolen += async (_, _) =>
        {
            if (!_screen.IsRunning || _userStopped) return;
            _tray.Notify("声音已恢复", "检测到其他投屏退出，音频已整路重启");
            await RestartForwardingCoreAsync(skipUnlock: true);
        };
        _poller.Start();
    }

    // ---------- 启动流程 ----------

    private async Task RunStartupAsync()
    {
        await Task.Delay(600); // v1 节奏：启动 600ms 后静默尝试
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await EnsureConnectedAsync();
            var devs = await Task.Run(() => _devices.RefreshMerged());
            _deviceCache = devs.ToList();
            if (_deviceCache.Count > 0)
            {
                await AutoStartAsync();
                return;
            }
            if (attempt < 2) await Task.Delay(4000); // 失败每 4 秒重试，最多两次
        }
        _tray.Notify("没有设备", "先连手机：USB 或无线调试；托盘 → 设备 → WiFi 无线可添加");
    }

    private async Task AutoStartAsync()
    {
        PickDefaultDevice();
        var wantScreen = _config.Settings.Screen;
        _config.Settings.Screen = false; // 启动先保证音频通路，再按需叠加画面
        await StartForwardingAsync(skipUnlock: false, userInitiated: false);
        if (_screen.IsRunning && wantScreen)
            await ToggleScreenAsync(skipUnlockIfEnabling: true);
        else if (!_screen.IsRunning)
        {
            _config.Settings.Screen = wantScreen; // 没启动成，恢复用户偏好
            _config.Save();
        }
    }

    private void PickDefaultDevice()
    {
        // 记忆的优先 → USB 优先 → 任意在线（对齐 v1 Get-Serial）
        var pick = _config.Settings.Serial;
        if (string.IsNullOrEmpty(pick) || !_deviceCache.Any(d => d.Serial == pick))
        {
            var usb = _deviceCache.FirstOrDefault(d => DeviceService.KindOf(d.Serial) == "USB");
            pick = (usb ?? _deviceCache.FirstOrDefault())?.Serial;
        }
        _screen.Serial = pick;
    }

    private async Task EnsureConnectedAsync()
    {
        await Task.Run(() =>
        {
            foreach (var t in _config.Settings.WifiTargets) _adb.Connect(t);
            foreach (var a in _adb.ListMdns()) _adb.Connect(a);
        });
    }

    // ---------- 启停核心（全部经 _gate 串行） ----------

    public void StartStopForwarding()
        => _ = _screen.IsRunning
            ? StopForwardingAsync(markUserStopped: true)
            : StartForwardingAsync(skipUnlock: false, userInitiated: true);

    private async Task StartForwardingAsync(bool skipUnlock, bool userInitiated)
    {
        if (_screen.IsRunning) return;
        if (!userInitiated && _userStopped) return; // 手动停止后不自动复活
        await _gate.WaitAsync();
        try
        {
            if (!_screen.IsRunning) await StartCoreAsync(skipUnlock);
        }
        finally { _gate.Release(); }
    }

    private async Task StopForwardingAsync(bool markUserStopped)
    {
        await _gate.WaitAsync();
        try
        {
            if (_screen.IsRunning)
            {
                _userStopped = markUserStopped;
                await StopCoreAsync();
            }
            else if (markUserStopped) _userStopped = true;
        }
        finally { _gate.Release(); }
    }

    private async Task RestartForwardingCoreAsync(bool skipUnlock)
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            await StartCoreAsync(skipUnlock);
        }
        finally { _gate.Release(); }
    }

    private async Task StartCoreAsync(bool skipUnlock)
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        if (string.IsNullOrEmpty(serial))
        {
            _tray.Notify("没有设备", "右键托盘 → 设备 → WiFi 无线可添加");
            return;
        }

        // 启动前先解锁，避免投出锁屏画面（spec：--turn-screen-off 生效后 adb 点不亮屏幕）
        if (!skipUnlock)
        {
            var ur = await UnlockWithPinPromptAsync(serial);
            if (!ur.Ok)
                _tray.Notify("没能自动解锁",
                    ur.Message == "NEED_PIN" ? "手机还在锁屏界面。可右键「唤醒并解锁」再试" : ur.Message);
        }

        _userStopped = false;
        var screen = _config.Settings.Screen;
        _keepAwake.ClearStale(serial);
        if (screen) _keepAwake.Push(serial);

        _poller.Serial = serial;
        _poller.IsRunning = true;
        _poller.SleepWatch = screen; // 纯音频不做息屏轮询
        _poller.ResetRatio();
        _poller.Start();

        if (!_screen.IsNative && screen)
        {
            // 嵌入模式：先立窗口、按视频比例定尺寸，再让 scrcpy 以内容区尺寸建在屏幕外
            EnsureMainWindow();
            _mainWindow!.Show();
            var work = SystemParameters.WorkArea;
            var (winW, winH) = _screen.DesiredWindowSize((int)work.Width, (int)work.Height, ChromeH, 0);
            var (cw, ch) = _mainWindow.ApplyWindowSize(winW, winH);
            Logger.Log($"embed start window {winW}x{winH} content {cw}x{ch}");
            _screen.Start(cw, ch);
            StartEmbedPolling();
        }
        else
        {
            _screen.Start();
            _mainWindow?.Hide();
        }

        _ = WakeAudioPathAsync(serial);
        _poller.OtherBaseline = await Task.Run(() => CountOtherScrcpy(ownRunning: true));
        UpdateStatus();
    }

    /// <summary>
    /// scrcpy 在 stderr 报告真实视频流尺寸（Initial texture）后重定窗口：
    /// adb 查到的虚拟屏/物理屏比例与实际流有出入时会产生 letterbox 黑边，以这里为准。
    /// stderr 线程回调，已由 Dispatcher 封送。
    /// </summary>
    private void OnVideoTexture(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        if (!_screen.IsRunning || _screen.IsNative || _mainWindow is null) return;
        var work = SystemParameters.WorkArea;
        var (winW, winH) = _screen.DesiredWindowSize((int)work.Width, (int)work.Height, ChromeH, 0, (double)w / h);
        var (cw, ch) = _mainWindow.ApplyWindowSize(winW, winH);
        Logger.Log($"texture {w}x{h} -> window {winW}x{winH} content {cw}x{ch}");
    }

    private async Task StopCoreAsync()
    {
        _embedTimer?.Stop();
        var serial = _screen.Serial;
        _mainWindow?.ShowSleepOverlay(false);
        _mainWindow?.Detach();
        _screen.Stop();
        _poller.Stop();
        if (!string.IsNullOrEmpty(serial))
        {
            await Task.Run(() => _keepAwake.Pop(serial));
            // 电源状态同步（对齐 v1）：屏幕亮着且锁着时补发一次 POWER，让显示电源与实际一致
            try
            {
                var st = _adb.GetScreenState(serial);
                if (st.Valid && st.On && st.Locked) _adb.SendKey(serial, 26);
            }
            catch { }
        }
        UpdateStatus();
    }

    /// <summary>转发启动 1.2 秒后发一次音量+ / 音量-，叫醒空闲手机的音频输出通路，音量前后不变。</summary>
    private async Task WakeAudioPathAsync(string serial)
    {
        try
        {
            await Task.Delay(1200);
            if (!_screen.IsRunning) return;
            await Task.Run(() =>
            {
                _adb.SendKey(serial, 24);
                _adb.SendKey(serial, 25);
            });
        }
        catch { }
    }

    private static int CountOtherScrcpy(bool ownRunning)
    {
        try
        {
            return Math.Max(0, Process.GetProcessesByName("scrcpy").Length - (ownRunning ? 1 : 0));
        }
        catch { return 0; }
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
            // 按我们设定的标题前缀找 v2 自己的窗口（FindWindowByPrefix 已校验进程归属）
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

    // ---------- 投屏开关 / 模式 / 置顶 ----------

    public void ToggleScreen() => _ = ToggleScreenAsync();

    private async Task ToggleScreenAsync(bool skipUnlockIfEnabling = false)
    {
        if (!_screen.IsRunning)
        {
            _config.Settings.Screen = true;
            _config.Save();
            await StartForwardingAsync(skipUnlock: false, userInitiated: true);
            return;
        }
        // 开关投屏 = 音频不中断的整路重启（画音同进程，这是可接受的代价）
        _config.Settings.Screen = !_config.Settings.Screen;
        _config.Save();
        var enable = _config.Settings.Screen;
        await RestartForwardingCoreAsync(skipUnlock: !enable || skipUnlockIfEnabling);
        _tray.Notify("投屏", enable ? "已开启手机画面" : "已回到纯音频转发");
    }

    public void SetWindowMode(string mode) => _ = SetModeAsync(mode);

    private async Task SetModeAsync(string mode)
    {
        if (_screen.IsNative == (mode == "native")) return;
        if (_screen.IsRunning)
        {
            await _gate.WaitAsync();
            try
            {
                await StopCoreAsync();
                _screen.SetMode(mode);
                await StartCoreAsync(skipUnlock: true);
            }
            finally { _gate.Release(); }
        }
        else _screen.SetMode(mode);
        _tray.Notify("窗口模式", mode == "native" ? "scrcpy 原生窗口" : "程序窗口");
        UpdateStatus();
    }

    public void ToggleTop() => _ = ToggleTopAsync();

    private async Task ToggleTopAsync()
    {
        _screen.SetOnTop(!_config.Settings.OnTop);
        if (_mainWindow is not null) _mainWindow.Topmost = _config.Settings.OnTop;
        // 原生模式的置顶要重启投屏才生效
        if (_screen.IsNative && _screen.IsRunning)
            await RestartForwardingCoreAsync(skipUnlock: true);
        UpdateStatus();
    }

    public void ResetWindowSize()
    {
        _screen.ResetSizes();
        _tray.Notify("窗口尺寸", "已重置，按屏幕默认重算");
    }

    // ---------- 唤醒解锁 ----------

    public void WakeUnlock() => DoWake();

    private async void DoWake()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        if (string.IsNullOrEmpty(serial)) return;

        UnlockService.Result r;
        if (_screen.IsRunning)
        {
            // 投屏时屏幕归 scrcpy 管，adb 点不亮：先停转发拿回屏幕 → 强唤醒解锁 → 重启转发
            await _gate.WaitAsync();
            try
            {
                await StopCoreAsync();
                await Task.Delay(400); // 等 scrcpy 释放显示电源
                r = await UnlockWithPinPromptAsync(serial, forceWake: true);
                await StartCoreAsync(skipUnlock: true); // 刚解完锁，跳过启动前解锁，避免解两次
            }
            finally { _gate.Release(); }
        }
        else
        {
            r = await UnlockWithPinPromptAsync(serial, forceWake: true);
        }

        if (!r.Ok && r.Message == "NEED_PIN")
            _tray.Notify("唤醒解锁失败", "没有已保存的密码或已取消录入；可点「忘记解锁密码」后重试");
        else if (r.Ok)
            _tray.Notify("唤醒解锁", r.Message);
        else
            _tray.Notify("唤醒解锁失败", r.Message);
    }

    /// <summary>解锁；遇到 NEED_PIN（首次没存密码）时弹一次输入框，保存后自动重试。取消则原样返回。</summary>
    private async Task<UnlockService.Result> UnlockWithPinPromptAsync(string serial, bool forceWake = false)
    {
        var r = await _unlock.InvokeAsync(serial, forceWake);
        if (r.Message != "NEED_PIN") return r;
        var win = new PinPromptWindow();
        win.ShowDialog();
        var pin = win.Result;
        if (string.IsNullOrEmpty(pin))
            return new UnlockService.Result(false, "NEED_PIN");
        _unlock.SavePin(pin);
        return await _unlock.InvokeAsync(serial, forceWake);
    }

    // ---------- 设备管理 ----------

    public void SelectDevice(string serial) => _ = SelectDeviceAsync(serial);

    private async Task SelectDeviceAsync(string serial)
    {
        _manualPick = serial; // 手动选过的设备不被插线自动切换抢走
        _screen.Serial = serial;
        if (_screen.IsRunning)
            await RestartForwardingCoreAsync(skipUnlock: true);
        else
            await StartForwardingAsync(skipUnlock: false, userInitiated: true);
        _tray.Notify("已切换设备", DeviceLabelOf(serial));
    }

    public void ConnectAddress(string addr) => _ = ConnectAddressAsync(addr);

    private async Task ConnectAddressAsync(string addr)
    {
        var r = await Task.Run(() => _devices.Connect(addr));
        var ok = r.Contains("connected", StringComparison.OrdinalIgnoreCase);
        _tray.Notify("连接设备", ok ? $"已连接 {addr}" : r);
        RefreshCacheAsync();
    }

    public async void AddDevice()
    {
        var (ok, addr, _) = InputDialog.Show("添加设备",
            "设备地址（如 192.168.1.29:45329，只填 IP 自动补 :5555）：");
        if (!ok || string.IsNullOrEmpty(addr)) return;
        await ConnectAddressAsync(addr);
    }

    public void RenameDevice()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        if (string.IsNullOrEmpty(serial))
        {
            _tray.Notify("重命名设备", "没有选中的设备");
            return;
        }
        var (ok, name, _) = InputDialog.Show("重命名此设备",
            "设备名称（留空恢复默认）：", initial1: DeviceLabelOf(serial));
        if (!ok) return; // 取消：保持不变
        _devices.Rename(serial, name);
        UpdateStatus();
        _tray.Notify("重命名设备",
            string.IsNullOrWhiteSpace(name) ? "已恢复默认名称" : $"已重命名为「{name}」");
    }

    public async void PairDevice()
    {
        var (ok, addr, code) = InputDialog.Show("配对新设备",
            "配对地址（手机「使用配对码配对设备」弹窗中的 IP:端口）：",
            "6 位配对码：");
        if (!ok || string.IsNullOrEmpty(addr) || string.IsNullOrEmpty(code)) return;
        var r = await Task.Run(() => _adb.Pair(addr, code));
        _tray.Notify("配对", r);
        if (r.Contains("success", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("paired", StringComparison.OrdinalIgnoreCase))
        {
            // 配对成功后自动扫描 mDNS 并连接可用设备（spec）
            await Task.Run(() =>
            {
                foreach (var a in _adb.ListMdns()) _adb.Connect(a);
            });
        }
        RefreshCacheAsync();
    }

    public async void AutoDiscover()
    {
        var addrs = await Task.Run(() => _adb.ListMdns());
        if (addrs.Count == 0)
        {
            _tray.Notify("自动发现", "mDNS 没发现设备，确认手机「无线调试」已开启");
            return;
        }
        var okCount = await Task.Run(() =>
        {
            var n = 0;
            foreach (var a in addrs)
                if (_devices.Connect(a).Contains("connected", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        });
        _tray.Notify("自动发现", $"发现 {addrs.Count} 台，连接成功 {okCount} 台");
        RefreshCacheAsync();
    }

    public async void ClearBadDevices()
    {
        if (_screen.IsRunning)
            await StopForwardingAsync(markUserStopped: true); // 先停转发，避免清理到自己正用的连接
        var n = await Task.Run(() => _devices.ClearBad());
        _tray.Notify("清理无效设备", n > 0 ? $"已断开 {n} 条无效连接" : "没有需要清理的连接");
        RefreshCacheAsync();
    }

    public void ForgetPin()
    {
        _unlock.ForgetPin();
        _tray.Notify("忘记解锁密码", "已清除，下次解锁时重新询问");
    }

    public async void ReconnectDevice()
    {
        _tray.Notify("重新连接", "正在连接已保存地址与 mDNS…");
        await EnsureConnectedAsync();
        RefreshCacheAsync();
        if (!_screen.IsRunning && !_userStopped)
            await StartForwardingAsync(skipUnlock: false, userInitiated: true);
    }

    public async void RefreshDeviceList()
    {
        var devs = await Task.Run(() => _devices.RefreshMerged());
        _deviceCache = devs.ToList();
        _tray.Notify("设备列表", _deviceCache.Count == 0 ? "没有在线设备" : $"在线设备 {_deviceCache.Count} 台");
    }

    private async void RefreshCacheAsync()
    {
        var devs = await Task.Run(() => _devices.RefreshMerged());
        _deviceCache = devs.ToList();
    }

    /// <summary>USB↔WiFi 自动切换（每 5 秒随设备列表刷新检查；对齐 v1 Update-Transport）。</summary>
    private async void TransportCheck()
    {
        if (!_screen.IsRunning) return;
        if ((DateTime.UtcNow - _lastSwitch).TotalSeconds < 5) return;
        var cur = _screen.Serial;
        if (string.IsNullOrEmpty(cur)) return;

        var curDev = _deviceCache.FirstOrDefault(d => d.Serial == cur);
        if (curDev is null)
        {
            // 当前设备掉线：回落到存活设备（USB 优先）
            var fb = _deviceCache.FirstOrDefault(d => DeviceService.KindOf(d.Serial) == "USB")
                     ?? _deviceCache.FirstOrDefault();
            if (fb is not null) await SwitchTransportAsync(fb.Serial, "当前设备已断开，已自动切换");
            return;
        }

        // 插线自动切换：WiFi 设备的同机型 USB 出现；机型不同不切，避免误切到另一台
        if (DeviceService.KindOf(cur) != "WiFi" || _manualPick is not null) return;
        if (string.IsNullOrEmpty(curDev.Model)) return;
        var usb = _deviceCache.FirstOrDefault(d =>
            DeviceService.KindOf(d.Serial) == "USB" && d.Model == curDev.Model);
        if (usb is not null) await SwitchTransportAsync(usb.Serial, "已切换到有线连接");
    }

    private async Task SwitchTransportAsync(string serial, string msg)
    {
        _lastSwitch = DateTime.UtcNow;
        _screen.Serial = serial;
        await RestartForwardingCoreAsync(skipUnlock: true);
        _tray.Notify("连接切换", $"{msg}（{DeviceLabelOf(serial)}）");
    }

    private string DeviceLabelOf(string serial)
    {
        var d = _deviceCache.FirstOrDefault(x => x.Serial == serial);
        return d is null ? serial : _devices.NameOf(d);
    }

    // ---------- 媒体 / 窗口 / 菜单 ----------

    public void MediaPause() => DoMediaPause();

    private async void DoMediaPause()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        var r = await Task.Run(() => _media.TogglePause(serial ?? ""));
        _tray.Notify("媒体暂停", r.Message);
    }

    private void OnTrayClicked(object? sender, WfMouseButtons button)
    {
        if (button != WfMouseButtons.Right) return;
        // 菜单用缓存的设备列表即时弹出，adb 刷新在后台进行，右键不冻结 UI
        var menu = AppMenus.BuildTrayMenu(this);
        menu.IsOpen = true;
    }

    private async Task OnTrayDoubleClicked()
    {
        if (_screen.IsRunning)
        {
            if (!_screen.IsNative && _mainWindow?.IsVisible == true)
                await StopForwardingAsync(markUserStopped: true);      // 运行中且窗口可见 → 停止
            else if (!_screen.IsNative)
                ShowMainWindow();                                       // 窗口隐藏 → 显示窗口
            else
                await StopForwardingAsync(markUserStopped: true);      // 原生模式无程序窗口 → 停止
        }
        else
        {
            await StartForwardingAsync(skipUnlock: false, userInitiated: true);
        }
    }

    private void EnsureMainWindow()
    {
        if (_mainWindow is not null) return;
        _mainWindow = new MainWindow();
        _mainWindow.SetCallbacks(
            onClose: () => _ = StopForwardingAsync(markUserStopped: false),
            onWake: DoWake,
            onSizeCommitted: (w, h) =>
            {
                // 拖动时每一帧都会触发，防抖后只记最终尺寸
                _sizeSaveTimer?.Stop();
                _sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _sizeSaveTimer.Tick += (_, _) =>
                {
                    _sizeSaveTimer!.Stop();
                    var r = _poller.CurrentRatio;
                    _screen.RememberSize(w, h, r > 0 ? r : null);
                };
                _sizeSaveTimer.Start();
            });
        RefreshWindowMenus();
    }

    public void ShowMainWindow()
    {
        if (_screen.IsNative)
        {
            _tray.Notify("窗口模式", "原生窗口模式下没有程序窗口，先切回「程序窗口」");
            return;
        }
        EnsureMainWindow();
        _mainWindow!.Show();
        _mainWindow.Activate();
    }

    public void ToggleFullscreen()
    {
        if (_mainWindow is null) return;
        _mainWindow.ToggleFullscreen();
    }

    /// <summary>窗口顶栏菜单与托盘共用 AppMenus；状态变化后重建。</summary>
    private void RefreshWindowMenus()
    {
        if (_mainWindow is null) return;
        _mainWindow.SetMenus(AppMenus.BuildTopBarRoots(this));
    }

    private void UpdateStatus()
    {
        _tray.SetActive(_screen.IsRunning);
        RefreshWindowMenus();
    }

    public void EditPauseKey() => EditHotkey("暂停", true);
    public void EditScreenKey() => EditHotkey("投屏", false);

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

    public void OpenSettings()
    {
        var win = new SettingsWindow(_config, () =>
        {
            // 画质参数改了要重启投屏才生效
            if (_screen.IsRunning)
                _ = RestartForwardingCoreAsync(skipUnlock: true);
        });
        win.ShowDialog();
        UpdateStatus();
    }

    public void ShowHotkeyHelp()
    {
        var s = _config.Settings;
        System.Windows.MessageBox.Show(
            $"暂停 / 继续（手机媒体）：{(string.IsNullOrWhiteSpace(s.HotkeyPause) ? "（禁用）" : s.HotkeyPause)}\n" +
            $"投屏开关：{(string.IsNullOrWhiteSpace(s.HotkeyScreen) ? "（禁用）" : s.HotkeyScreen)}\n" +
            $"全屏：主窗口内按 F11，Esc 退出\n\n" +
            "单键快捷键会全局占用该按键；可在托盘 → 快捷键 里修改或禁用。",
            "快捷键说明");
    }

    public void ShowLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Logger.LogPath}\""));
        }
        catch (Exception ex)
        {
            _tray.Notify("查看日志", $"打不开日志：{ex.Message}");
        }
    }

    public void About()
    {
        var v = typeof(App).Assembly.GetName().Version;
        System.Windows.MessageBox.Show(
            $"手机音频托盘 v2\n版本 {v?.ToString(3)}\n手机声音转发 · 投屏 · 唤醒解锁",
            "关于手机音频托盘");
    }

    // ---------- 状态展示 ----------

    public bool Running => _screen.IsRunning;
    public bool ScreenOn => _config.Settings.Screen && _screen.IsRunning;
    public bool NativeMode => _screen.IsNative;
    public bool OnTop => _config.Settings.OnTop;
    public string PauseKeyText => _config.Settings.HotkeyPause;
    public string ScreenKeyText => _config.Settings.HotkeyScreen;

    public string StatusText
    {
        get
        {
            if (!_screen.IsRunning) return "○ 已停止";
            var s = _config.Settings;
            var mode = !s.Screen ? "纯音频" : (_screen.IsNative ? "原生窗口" : "程序窗口");
            var fps = s.MaxFps > 0 ? $"{s.MaxFps}fps" : "不限帧";
            return $"● 运行中 | {mode} | {s.Codec} {s.Buffer}ms {fps}";
        }
    }

    public IReadOnlyList<(string label, string kind, Action click)> DeviceItems
        => _deviceCache
            .Select(d => (DeviceLabelOf(d.Serial), DeviceService.KindOf(d.Serial),
                          (Action)(() => SelectDevice(d.Serial))))
            .ToList();

    public IReadOnlyList<string> SavedWifiOffline
        => _config.Settings.WifiTargets
            .Where(a => !_deviceCache.Any(d => string.Equals(d.Serial, a, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    // ---------- 退出 / 自检 ----------

    public async void ExitApp()
    {
        try
        {
            _embedTimer?.Stop();
            var serial = _screen.Serial;
            _mainWindow?.Detach();
            _screen.Stop();
            _poller.Stop();
            if (!string.IsNullOrEmpty(serial)) await Task.Run(() => _keepAwake.Pop(serial));
        }
        catch (Exception ex) { Logger.Log($"exit cleanup: {ex.Message}"); }
        finally
        {
            try { _hotkeys.Dispose(); _tray.Dispose(); } catch { }
            // 配置统一由 OnExit 保存，这里不再双写
            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            Shutdown(0);
        }
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
            System.Console.WriteLine($"[selftest]   screenSize={(ss.Length == 2 ? $"{ss[0]}x{ss[1]}" : "读不到")}");
            var mdns = adb.ListMdns();
            System.Console.WriteLine($"[selftest]   mdns={string.Join(",", mdns)}");
            System.Console.WriteLine($"[selftest]   mediaSession={adb.HasMediaSession(d.Serial)}");
        }
        System.Console.WriteLine("[selftest] merged devices: " +
            DeviceService.MergeDuplicates(adb.ListDevices()).Count);
        System.Console.WriteLine("[selftest] hotkey parse Ctrl+Alt+P: " +
            HotkeyParser.TryParse("Ctrl+Alt+P", out var m, out var k) + $" mods={m} key={k}");
        System.Console.WriteLine("[selftest] hotkey parse Pause: " +
            HotkeyParser.TryParse("Pause", out var m2, out var k2) + $" mods={m2} key={k2}");
        System.Console.WriteLine("[selftest] hotkey parse Ctrl+A+B (应拒绝): " +
            HotkeyParser.TryParse("Ctrl+A+B", out _, out _));
        System.Console.WriteLine("[selftest] args(native=false):");
        System.Console.WriteLine(ScrcpyProcess.GetArgsForTest(new ScrcpyOptions
        {
            Serial = "TEST", Screen = true, NativeWindow = false,
            MaxFps = 60, VideoBitRate = 16000000, VideoCodec = "h265",
            WindowTitle = "ScrcpyBridge|TEST", WindowWidth = 400, WindowHeight = 880,
        }));
        System.Console.WriteLine("[selftest] args(audio-only):");
        System.Console.WriteLine(ScrcpyProcess.GetArgsForTest(new ScrcpyOptions
        {
            Serial = "TEST", Screen = false,
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _config?.Save(); } catch { }
        base.OnExit(e);
    }
}
