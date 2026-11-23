using System.IO;
using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 跨设备通用解锁：dismiss-keyguard → 回车 → 比例上滑 → PIN。
/// 每步后查锁屏状态，成功即停。必须在 scrcpy 未接管屏幕时调用。
/// 全流程同步实现，通过 InvokeAsync 在后台线程执行，不阻塞 UI。
/// </summary>
public sealed class UnlockService
{
    private readonly AdbClient _adb;
    private readonly string _pinPath;

    public UnlockService(AdbClient adb)
    {
        _adb = adb;
        _pinPath = Path.Combine(AppContext.BaseDirectory, "device-pin.json");
    }

    public record Result(bool Ok, string Message);

    /// <summary>整个解锁流程放到后台线程跑，避免 UI 冻结（内有多次 adb 往返与等待）。</summary>
    public Task<Result> InvokeAsync(string serial, bool forceWake = false)
        => Task.Run(() => Invoke(serial, forceWake));

    private string? ReadPin()
    {
        var d = JsonStore.Read<Dictionary<string, string>>(_pinPath);
        return d is not null && d.TryGetValue("pin", out var pin) ? pin : null;
    }

    public void SavePin(string pin)
    {
        try { JsonStore.Write(_pinPath, new Dictionary<string, string> { ["pin"] = pin }); }
        catch (Exception ex) { Logger.Log($"save pin failed: {ex.Message}"); }
    }

    /// <summary>「忘记解锁密码」：清除已存 PIN，下次解锁重新询问。</summary>
    public void ForgetPin()
    {
        try { if (File.Exists(_pinPath)) File.Delete(_pinPath); }
        catch (Exception ex) { Logger.Log($"forget pin failed: {ex.Message}"); }
    }

    private bool WaitUnlocked(string serial, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (!_adb.GetScreenState(serial).Locked) return true;
            Thread.Sleep(120);
        }
        return false;
    }

    /// <summary>把密码框弄出来。三级手势，任一成功返回 true。</summary>
    private bool ShowKeyguard(string serial)
    {
        if (WaitUnlocked(serial, 500)) return true;

        _adb.DismissKeyguard(serial);
        if (WaitUnlocked(serial, 600)) return true;

        _adb.SendKey(serial, 66); // ENTER
        if (WaitUnlocked(serial, 600)) return true;

        // 上滑坐标按屏幕比例算，不写死像素；读不到尺寸就跳过这步
        var wh = _adb.GetScreenSize(serial);
        if (wh.Length != 2) wh = _adb.GetVideoSize(serial);
        if (wh.Length == 2)
        {
            int cx = wh[0] / 2;
            _adb.ShellRaw(serial,
                $"input swipe {cx} {(int)(wh[1] * 0.88)} {cx} {(int)(wh[1] * 0.28)} 180");
        }
        return WaitUnlocked(serial, 700);
    }

    private void SendPinKeys(string serial, string pin)
    {
        var parts = new List<string>();
        foreach (var ch in pin)
        {
            if (char.IsDigit(ch))
                parts.Add($"input keyevent {ch - '0' + 7}"); // KEYCODE_0=7
            else if (char.IsLetter(ch))
                parts.Add($"input keyevent {char.ToUpperInvariant(ch) - 'A' + 29}"); // KEYCODE_A=29
        }
        if (parts.Count == 0) return;
        _adb.ShellRaw(serial, string.Join("; sleep 0.05; ", parts));
    }

    public Result Invoke(string serial, bool forceWake = false)
    {
        var st = _adb.GetScreenState(serial);
        if (!st.Valid) return new Result(false, "读不到手机状态，可能已断开");

        if (!st.Locked && !forceWake) return new Result(true, "已解锁");

        if (st.On)
        {
            // 显示电源可能被 scrcpy 关过（物理黑、状态假报）：POWER 关一次再 WAKEUP
            _adb.SendKey(serial, 26);
            Thread.Sleep(420);
            _adb.SendKey(serial, 224);
        }
        else
        {
            _adb.SendKey(serial, 26);
        }
        Thread.Sleep(1400);

        if (ShowKeyguard(serial)) return new Result(true, "已唤醒解锁");

        var pin = ReadPin();
        if (string.IsNullOrEmpty(pin)) return new Result(false, "NEED_PIN");

        SendPinKeys(serial, pin);
        if (WaitUnlocked(serial, 800)) return new Result(true, "已唤醒解锁");

        // vivo 等机型不会自动提交，补一个回车
        _adb.SendKey(serial, 66);
        return WaitUnlocked(serial, 1300)
            ? new Result(true, "已唤醒解锁")
            : new Result(false, "解锁失败，密码可能不对");
    }
}
