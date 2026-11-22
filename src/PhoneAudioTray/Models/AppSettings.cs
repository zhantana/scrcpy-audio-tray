namespace PhoneAudioTray.Models;

/// <summary>
/// 全部设置项。字段名与 v1 的 settings.json 保持一致，保证配置能直接读入。
/// </summary>
public class AppSettings
{
    public int Buffer { get; set; } = 40;
    public string Codec { get; set; } = "raw";
    public string? Serial { get; set; }
    public bool Screen { get; set; }
    public bool OnTop { get; set; }
    public int MaxFps { get; set; } = 60;
    public List<string> WifiTargets { get; set; } = new();
    public string HotkeyPause { get; set; } = "Ctrl+Alt+P";
    public string HotkeyScreen { get; set; } = "F7";
    public int[] SizePortrait { get; set; } = Array.Empty<int>();
    public int[] SizeLandscape { get; set; } = Array.Empty<int>();
    public string WindowMode { get; set; } = "embedded";

    // v2 新增：画质
    public int VideoBitRate { get; set; } = 16000000;
    public string VideoCodec { get; set; } = "h264";
    public int MaxSize { get; set; } = 0; // 0 = 不降分辨率
}
