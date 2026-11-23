using System.Diagnostics;
using System.Windows.Threading;
using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 统一轮询：单 1 秒节拍，各周期任务挂在不同计数上（对齐 v1 的单 Timer 结构，
/// 不给每个功能各建一个 Timer）。adb 查询在后台线程执行，事件经 Dispatcher
/// 封送回 UI 线程。上一拍没跑完时跳过本拍，避免命令堆积。
/// </summary>
public sealed class DevicePoller : IDisposable
{
    private readonly AdbClient _adb;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private long _tick;
    private volatile bool _busy;
    private bool _started;

    private string? _serial;
    private int _asleepVotes;
    private bool _asleep;
    private double _lastRatio;
    private int _otherBaseline = -1;
    private int _lowVotes;

    public event EventHandler<bool>? AsleepChanged;      // 手机息屏（连续 2 次判定）
    public event EventHandler<double>? OrientationChanged; // 视频宽高比变化（0.02 容差）
    public event EventHandler? SoundStolen;               // 其他 scrcpy 退出，音频通路被收走
    public event EventHandler<IReadOnlyList<AdbClient.DeviceInfo>>? DevicesRefreshed; // 每 5 秒

    public DevicePoller(AdbClient adb, Dispatcher dispatcher)
    {
        _adb = adb;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public string? Serial { get => _serial; set { _serial = value; _asleepVotes = 0; } }

    /// <summary>转发是否在跑（决定息屏/方向/实例计数任务是否执行；设备列表刷新恒跑）。</summary>
    public bool IsRunning { get; set; }

    /// <summary>是否监视手机息屏——纯音频模式没有画面，不存在冻结问题，不监视（对齐 spec）。</summary>
    public bool SleepWatch { get; set; }

    /// <summary>除自己以外的 scrcpy 实例基线；-1 表示尚未记录。转发启动后由上层设置。</summary>
    public int OtherBaseline { get => _otherBaseline; set { _otherBaseline = value; _lowVotes = 0; } }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _tick = 0;
        _timer.Start();
    }

    public void Stop()
    {
        IsRunning = false;
        if (!_started) return;
        _started = false;
        _timer.Stop();
        if (_asleep) { _asleep = false; AsleepChanged?.Invoke(this, false); }
    }

    /// <summary>重新开始记录方向基线（重启转发后调用，避免上一轮的比例误报变化）。</summary>
    public void ResetRatio() => _lastRatio = 0;

    /// <summary>最近一次读到的视频宽高比；尚未读到时为 0。</summary>
    public double CurrentRatio => _lastRatio;

    private void Tick()
    {
        if (_busy) return;
        _tick++;
        var serial = _serial;
        var watch = IsRunning && !string.IsNullOrEmpty(serial);
        var doDevices = _tick % 5 == 0;
        if (!watch && !doDevices) return;

        _busy = true;
        var tick = _tick;
        var running = IsRunning;
        Task.Run(() =>
        {
            try
            {
                if (doDevices) RefreshDevices();
                if (watch)
                {
                    if (SleepWatch) CheckSleep(serial!);
                    if (running && tick % 2 == 0) CheckOrientation(serial!);
                    if (running) CheckOtherScrcpy();
                }
            }
            finally { _busy = false; }
        });
    }

    private void CheckSleep(string serial)
    {
        var st = _adb.GetScreenState(serial);
        if (!st.Valid) return;
        Post(() =>
        {
            if (st.On)
            {
                _asleepVotes = 0;
                if (_asleep) { _asleep = false; AsleepChanged?.Invoke(this, false); }
            }
            else
            {
                _asleepVotes++;
                if (!_asleep && _asleepVotes >= 2)
                {
                    _asleep = true;
                    Logger.Log("screen asleep detected");
                    AsleepChanged?.Invoke(this, true);
                }
            }
        });
    }

    private void CheckOrientation(string serial)
    {
        var v = _adb.GetVideoSize(serial);
        if (v.Length != 2 || v[0] <= 0 || v[1] <= 0) return;
        var ratio = (double)v[0] / v[1];
        if (_lastRatio != 0 && Math.Abs(ratio - _lastRatio) > 0.02)
        {
            Logger.Log($"orientation changed: {_lastRatio:F3} -> {ratio:F3} ({v[0]}x{v[1]})");
            Post(() => OrientationChanged?.Invoke(this, ratio));
        }
        _lastRatio = ratio;
    }

    private void RefreshDevices()
    {
        var devs = _adb.ListDevices();
        Post(() => DevicesRefreshed?.Invoke(this, devs));
    }

    private void CheckOtherScrcpy()
    {
        int total;
        try { total = Process.GetProcessesByName("scrcpy").Length; }
        catch { return; }
        var others = Math.Max(0, total - (IsRunning ? 1 : 0));
        if (_otherBaseline < 0) { _otherBaseline = others; return; }
        if (others < _otherBaseline)
        {
            _lowVotes++;
            if (_lowVotes >= 2)
            {
                _lowVotes = 0;
                _otherBaseline = -1; // 等上层重启后重新记基线
                Logger.Log($"other scrcpy exited: baseline dropped, will restart audio");
                Post(() => SoundStolen?.Invoke(this, EventArgs.Empty));
            }
        }
        else _lowVotes = 0;
    }

    private void Post(Action action) => _dispatcher.BeginInvoke(action);
    public void Dispose() => _timer.Stop();
}
