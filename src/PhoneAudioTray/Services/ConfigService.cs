using System.IO;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Models;

namespace PhoneAudioTray.Services;

/// <summary>
/// 设置读写 + v1 兼容迁移。路径取程序所在目录，跟随安装位置。
/// </summary>
public sealed class ConfigService
{
    private readonly string _settingsPath;
    public AppSettings Settings { get; private set; } = new();

    /// <summary>本次 Load 读到了损坏的配置文件（已留档，值回落默认）。</summary>
    public bool LoadCorrupt { get; private set; }

    public ConfigService()
    {
        var dir = AppContext.BaseDirectory;
        _settingsPath = Path.Combine(dir, "settings.json");
        JsonStore.Corrupted += path =>
        {
            if (path == _settingsPath) LoadCorrupt = true;
        };
    }

    public void Load()
    {
        var old = JsonStore.Read<AppSettings>(_settingsPath);
        if (old is null) return;

        Settings = old;

        // v1 遗留垃圾值回落
        if (string.Equals(Settings.HotkeyPause, "None", StringComparison.OrdinalIgnoreCase))
            Settings.HotkeyPause = "Ctrl+Alt+P";
        if (string.Equals(Settings.HotkeyScreen, "None", StringComparison.OrdinalIgnoreCase))
            Settings.HotkeyScreen = "F7";
        if (Settings.MaxFps != 0 && Settings.MaxFps < 15) Settings.MaxFps = 60;
        if (Settings.WindowMode is not ("embedded" or "native")) Settings.WindowMode = "embedded";
        if (Settings.VideoCodec is not ("h264" or "h265" or "av1")) Settings.VideoCodec = "h264";
        Settings.WifiTargets ??= new List<string>();
    }

    public void Save()
    {
        try { JsonStore.Write(_settingsPath, Settings); }
        catch (Exception ex) { Logger.Log($"save settings failed: {ex.Message}"); }
    }
}
