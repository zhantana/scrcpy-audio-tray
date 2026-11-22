using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 两个全局热键：媒体暂停、投屏开关。互相独立，重复键拒绝注册。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly ConfigService _config;
    private readonly HotkeyWindow _window;
    private const int IdPause = 0x5150;
    private const int IdScreen = 0x5151;

    public event EventHandler? PausePressed;
    public event EventHandler? ScreenTogglePressed;
    public event EventHandler<string>? RegisterFailed;

    public HotkeyService(ConfigService config)
    {
        _config = config;
        _window = new HotkeyWindow();
        _window.HotkeyPressed += OnHotkey;
    }

    private void OnHotkey(object? sender, int id)
    {
        if (id == IdPause) PausePressed?.Invoke(this, EventArgs.Empty);
        else if (id == IdScreen) ScreenTogglePressed?.Invoke(this, EventArgs.Empty);
    }

    public void RegisterAll()
    {
        _window.Unregister(IdPause);
        _window.Unregister(IdScreen);

        var pause = _config.Settings.HotkeyPause;
        var screen = _config.Settings.HotkeyScreen;

        // 两键相同只保留暂停键，提示用户
        if (!string.IsNullOrWhiteSpace(pause) &&
            string.Equals(pause, screen, StringComparison.OrdinalIgnoreCase))
        {
            screen = "";
            RegisterFailed?.Invoke(this, "投屏开关键与暂停键相同，已跳过投屏键");
        }

        RegisterOne(IdPause, pause, "暂停键");
        RegisterOne(IdScreen, screen, "投屏开关键");
    }

    private void RegisterOne(int id, string? spec, string label)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        if (!HotkeyParser.TryParse(spec, out var mods, out var key))
        {
            RegisterFailed?.Invoke(this, $"{label}「{spec}」写法不对");
            return;
        }
        if (!_window.Register(id, mods, key))
            RegisterFailed?.Invoke(this, $"{label}「{spec}」被别的软件占用");
    }

    public void SetPause(string spec)
    {
        _config.Settings.HotkeyPause = spec.Trim();
        _config.Save();
        RegisterAll();
    }

    public void SetScreen(string spec)
    {
        _config.Settings.HotkeyScreen = spec.Trim();
        _config.Save();
        RegisterAll();
    }

    public void Dispose()
    {
        _window.Unregister(IdPause);
        _window.Unregister(IdScreen);
        _window.DestroyHandle();
    }
}
