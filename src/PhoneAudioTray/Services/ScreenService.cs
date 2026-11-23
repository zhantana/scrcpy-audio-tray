using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Models;

namespace PhoneAudioTray.Services;

/// <summary>
/// 投屏控制：开窗、停窗、置顶、窗口尺寸记忆、方向跟随。
/// UI 无关的纯逻辑，便于测试。
/// </summary>
public sealed class ScreenService
{
    private readonly ConfigService _config;
    private readonly AdbClient _adb;
    private readonly ScrcpyProcess _proc;
    private string? _serial;

    public event EventHandler? StateChanged;

    public bool IsRunning => _proc.IsRunning;
    public bool IsNative => _config.Settings.WindowMode == "native";

    public ScreenService(ConfigService config, AdbClient adb, ScrcpyProcess proc)
    {
        _config = config;
        _adb = adb;
        _proc = proc;
        _proc.Exited += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string? Serial
    {
        get => _serial;
        set { _serial = value; _config.Settings.Serial = value; _config.Save(); }
    }

    public ScrcpyOptions BuildOptions(int windowWidth = 0, int windowHeight = 0)
    {
        var s = _config.Settings;
        var serial = _serial ?? s.Serial ?? "";
        var title = IsNative ? $"手机投屏 - {serial}" : $"ScrcpyBridge|{serial}";

        return new ScrcpyOptions
        {
            Serial = serial,
            Screen = s.Screen,
            NativeWindow = IsNative,
            OnTop = s.OnTop,
            AudioCodec = s.Codec,
            AudioBuffer = s.Buffer,
            MaxFps = s.MaxFps,
            VideoBitRate = s.VideoBitRate,
            VideoCodec = s.VideoCodec,
            MaxSize = s.MaxSize,
            WindowTitle = title,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
        };
    }

    public void Start(int windowWidth = 0, int windowHeight = 0)
    {
        if (string.IsNullOrEmpty(_serial ?? _config.Settings.Serial)) return;
        _proc.Start(BuildOptions(windowWidth, windowHeight));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _proc.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>视频流宽高比。读不到回退物理屏，再读不到按竖屏默认 0.45（1080x2400 量级）。</summary>
    public double GetVideoRatio()
    {
        if (string.IsNullOrEmpty(_serial)) return 0.45;
        var v = _adb.GetVideoSize(_serial);
        if (v.Length == 2 && v[0] > 0 && v[1] > 0) return (double)v[0] / v[1];
        var p = _adb.GetScreenSize(_serial);
        if (p.Length == 2 && p[0] > 0 && p[1] > 0) return (double)p[0] / p[1];
        return 0.45;
    }

    /// <summary>按方向和记忆尺寸算出窗口应有多大。工作区不超过屏幕 95%。
    /// ratio 已知（轮询传来）时直接用，避免再打一条 adb。</summary>
    public (int w, int h) DesiredWindowSize(int workWidth, int workHeight, int chromeH, int inset, double? ratio = null)
    {
        var r = ratio ?? GetVideoRatio();
        bool portrait = r < 1;
        var saved = portrait ? _config.Settings.SizePortrait : _config.Settings.SizeLandscape;
        if (saved.Length == 2 && saved[0] > 200 && saved[1] > 200)
            return (saved[0], saved[1]);

        // 全部按 DIP 计算：入口的 workWidth/workHeight 来自 SystemParameters.WorkArea，
        // 出口交给 Window.Width/Height，二者都是 DIP；物理像素只在嵌入 Win32 子窗口时换算。
        int maxH = (int)(workHeight * 0.95);
        int w, h;
        if (portrait)
        {
            w = 416;
            h = (int)((w - 2 * inset) / r) + chromeH + 2 * inset;
            if (h > maxH) { h = maxH; w = (int)((h - chromeH - 2 * inset) * r) + 2 * inset; }
        }
        else
        {
            w = (int)(workWidth * 0.95);
            h = (int)((w - 2 * inset) / r) + chromeH + 2 * inset;
            if (h > maxH) { h = maxH; w = (int)((h - chromeH - 2 * inset) * r) + 2 * inset; }
        }
        return (w, h);
    }

    public void RememberSize(int w, int h, double? ratio = null)
    {
        if (IsNative) return;
        var size = new[] { w, h };
        var r = ratio ?? GetVideoRatio();
        var portrait = r < 1;
        if (portrait) _config.Settings.SizePortrait = size;
        else _config.Settings.SizeLandscape = size;
        _config.Save();
        Logger.Log($"size {(portrait ? "portrait" : "landscape")}={w}x{h}");
    }

    public void SetMode(string mode)
    {
        _config.Settings.WindowMode = mode == "native" ? "native" : "embedded";
        _config.Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetOnTop(bool on)
    {
        _config.Settings.OnTop = on;
        _config.Save();
    }

    public void ResetSizes()
    {
        _config.Settings.SizePortrait = Array.Empty<int>();
        _config.Settings.SizeLandscape = Array.Empty<int>();
        _config.Save();
    }
}
