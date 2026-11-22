using System.Windows.Threading;
using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 每秒检测手机是否息屏。连续两次判定为息屏才通知（防抖），
/// 避免解锁瞬间误弹提示。
/// </summary>
public sealed class SleepMonitor
{
    private readonly AdbClient _adb;
    private readonly DispatcherTimer _timer;
    private string? _serial;
    private bool _running;
    private int _asleepVotes;
    private bool _asleep;

    public event EventHandler<bool>? AsleepChanged;

    public SleepMonitor(AdbClient adb)
    {
        _adb = adb;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Check();
    }

    public string? Serial
    {
        get => _serial;
        set { _serial = value; _asleepVotes = 0; }
    }

    public bool IsAsleep => _asleep;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _asleepVotes = 0;
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
        if (_asleep) { _asleep = false; AsleepChanged?.Invoke(this, false); }
    }

    private void Check()
    {
        if (!_running || string.IsNullOrEmpty(_serial)) return;
        var state = _adb.GetScreenState(_serial);
        if (!state.Valid) return;

        if (state.On)
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
                AsleepChanged?.Invoke(this, true);
                Logger.Log("screen asleep detected");
            }
        }
    }
}
