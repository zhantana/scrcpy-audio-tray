namespace PhoneAudioTray.Models;

/// <summary>启动一次 scrcpy 所需的全部参数。集中构造，避免散落。</summary>
public sealed class ScrcpyOptions
{
    public required string Serial { get; init; }
    public bool Screen { get; init; }
    public bool NativeWindow { get; init; }
    public bool OnTop { get; init; }

    public string AudioCodec { get; init; } = "raw";
    public int AudioBuffer { get; init; } = 40;
    public int AudioBitRate { get; init; } = 192000;

    public int MaxFps { get; init; } = 60;
    public int VideoBitRate { get; init; } = 16000000;
    public string VideoCodec { get; init; } = "h264";
    public int MaxSize { get; init; } = 0;

    // 嵌入模式用
    public int WindowWidth { get; init; }
    public int WindowHeight { get; init; }
    public string WindowTitle { get; init; } = "";
}
