using System.IO;
using System.Windows;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Models;
using PhoneAudioTray.Services;

namespace PhoneAudioTray;

public partial class App : System.Windows.Application
{
    private ConfigService? _config;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // 自检：只打印构造的参数和设备信息，不建窗口、不常驻。
        if (e.Args.Contains("--selftest"))
        {
            SelfTest();
            Shutdown(0);
            return;
        }

        _config = new ConfigService();
        _config.Load();
    }

    private void SelfTest()
    {
        var dir = AppContext.BaseDirectory;
        var scrcpy = AdbClient.FindScrcpy(dir);
        Console.WriteLine($"[selftest] scrcpy: {scrcpy ?? "未找到"}");
        if (scrcpy is null) return;

        var adb = new AdbClient(scrcpy);
        foreach (var d in adb.ListDevices())
        {
            Console.WriteLine($"[selftest] device: {d.Serial} model={d.Model}");
            var vs = adb.GetVideoSize(d.Serial);
            Console.WriteLine($"[selftest] videoSize: {(vs.Length == 2 ? $"{vs[0]}x{vs[1]}" : "读不到")}");
            var ss = adb.GetScreenSize(d.Serial);
            Console.WriteLine($"[selftest] screenSize: {ss[0]}x{ss[1]}");
        }

        var opts = new ScrcpyOptions
        {
            Serial = "TEST",
            Screen = true,
            NativeWindow = false,
            MaxFps = 60,
            VideoBitRate = 16000000,
            VideoCodec = "h265",
            WindowTitle = "ScrcpyBridge|TEST",
            WindowWidth = 400,
            WindowHeight = 880,
        };
        Console.WriteLine("[selftest] args(native=false):");
        Console.WriteLine(ScrcpyProcess.GetArgsForTest(opts));
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _config?.Save();
        base.OnExit(e);
    }
}
