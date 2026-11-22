using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Services;
using PhoneAudioTray.UI;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            onShowWindow: ShowMainWindow,
            onResetSize: () => { _screen.ResetSizes(); _tray.Notify("窗口尺寸", "已重置"); },
            onExit: ExitApp,
            running: _screen.IsRunning,
            screenOn: _config.Settings.Screen,
            native: _screen.IsNative,
            onTop: _config.Settings.OnTop,
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
        if (_screen.IsRunning)
        {
            _screen.Stop();
            return;
        }
        _config.Settings.Screen = true;
        _screen.Start();
    }

    private void DoMediaPause()
    {
        var serial = _screen.Serial ?? _config.Settings.Serial;
        var r = _media.TogglePause(serial ?? "");
        _tray.Notify("媒体暂停", r.Message);
    }

    private void ShowMainWindow()
    {
        var win = new UI.Views.MainWindow();
        win.Show();
    }

    private void ExitApp()
    {
        _screen.Stop();
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
