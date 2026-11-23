using System.Diagnostics;
using System.IO;
using System.Text;
using PhoneAudioTray.Models;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// 管理 scrcpy 进程的启动与停止。画面和声音在同一进程（拆分会导致音频丢样本）。
/// </summary>
public sealed class ScrcpyProcess : IDisposable
{
    private Process? _process;
    private readonly string _exe;

    public event EventHandler? Exited;

    public bool IsRunning
    {
        get
        {
            try { return _process is not null && !_process.HasExited; }
            catch { return false; }
        }
    }

    public ScrcpyProcess(string exe) => _exe = exe;

    public void Start(ScrcpyOptions o)
    {
        // 防御：运行中重复 Start 会覆盖旧进程引用，scrcpy 变孤儿。先停旧的。
        Stop();
        var args = BuildArgs(o);
        var psi = new ProcessStartInfo(_exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true, // 只藏控制台，画面窗口照常创建
            WorkingDirectory = Path.GetDirectoryName(_exe) ?? "",
        };
        _process = Process.Start(psi);
        if (_process is not null)
        {
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        try { _process?.Dispose(); } catch { }
        _process = null;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>集中构造所有 scrcpy 参数。</summary>
    internal static string BuildArgs(ScrcpyOptions o)
    {
        var a = new List<string>
        {
            "-s", o.Serial,
            "--audio-source=output",
            $"--audio-codec={o.AudioCodec}",
            $"--audio-buffer={o.AudioBuffer}",
            "--audio-output-buffer=10",
            "--require-audio",
        };

        if (o.Screen)
        {
            // 投屏时关手机屏；鼠标侧键改为返回，避免误触最近任务
            a.Add("--turn-screen-off");
            a.Add("--mouse-bind=bhb-:++++");

            if (o.MaxFps > 0) a.Add($"--max-fps={o.MaxFps}");

            // v2 画质：码率 / 编码格式 / 分辨率
            a.Add($"--video-bit-rate={o.VideoBitRate}");
            if (o.VideoCodec != "h264") a.Add($"--video-codec={o.VideoCodec}");
            if (o.MaxSize > 0) a.Add($"--max-size={o.MaxSize}");

            if (o.NativeWindow)
            {
                if (o.OnTop) a.Add("--always-on-top");
                a.Add($"--window-title={o.WindowTitle}");
            }
            else
            {
                // 嵌入：建在屏幕外，避免先冒出来再被收走
                a.Add("--window-x=-32000");
                a.Add("--window-y=-32000");
                a.Add($"--window-title={o.WindowTitle}");
                if (o.WindowWidth >= 120 && o.WindowHeight >= 120)
                {
                    a.Add($"--window-width={o.WindowWidth}");
                    a.Add($"--window-height={o.WindowHeight}");
                }
            }
        }
        else
        {
            // 纯音频
            a.Add("--no-video");
            a.Add("--no-window");
        }

        if (o.AudioCodec != "raw") a.Add($"--audio-bit-rate={o.AudioBitRate}");

        return string.Join(" ", a.Select(Quote));
    }

    /// <summary>供自检输出参数，便于核对。公开包装内部实现。</summary>
    public static string GetArgsForTest(ScrcpyOptions o) => BuildArgs(o);

    private static string Quote(string s)
    {
        if (!s.Any(char.IsWhiteSpace) && !s.Contains('"')) return s;
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }

    public void Stop()
    {
        try
        {
            if (IsRunning)
            {
                _process!.CloseMainWindow();
                if (!_process.WaitForExit(1500))
                {
                    _process.Kill();
                    _process.WaitForExit(800);
                }
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Stop();
}
