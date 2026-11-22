using System.IO;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Models;

namespace PhoneAudioTray.Services;

/// <summary>
/// 设备管理：列表、别名、连接方式判定、记忆地址、无效清理。
/// </summary>
public sealed class DeviceService
{
    private readonly AdbClient _adb;
    private readonly ConfigService _config;
    private readonly string _namesPath;
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public DeviceService(AdbClient adb, ConfigService config)
    {
        _adb = adb;
        _config = config;
        _namesPath = Path.Combine(AppContext.BaseDirectory, "device-names.json");
    }

    public void LoadNames()
    {
        var d = JsonStore.Read<Dictionary<string, string>>(_namesPath);
        if (d is not null) _names = d;
    }

    public void SaveNames() => JsonStore.Write(_namesPath, _names);

    public IReadOnlyList<AdbClient.DeviceInfo> Refresh() => _adb.ListDevices();

    public static string KindOf(string serial)
        => (serial.Contains(':') || serial.Contains("_adb-tls")) ? "WiFi" : "USB";

    public string NameOf(AdbClient.DeviceInfo d)
    {
        if (_names.TryGetValue(d.Serial, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (!string.IsNullOrEmpty(d.Model) && _names.TryGetValue(d.Model, out n) && !string.IsNullOrWhiteSpace(n)) return n;
        return d.Model ?? d.Serial;
    }

    public void Rename(string serial, string name)
    {
        _names[serial] = name.Trim();
        SaveNames();
    }

    public void AddWifiTarget(string addr)
    {
        addr = addr.Trim();
        if (string.IsNullOrEmpty(addr)) return;
        var list = new List<string> { addr };
        foreach (var t in _config.Settings.WifiTargets)
            if (!string.Equals(t, addr, StringComparison.OrdinalIgnoreCase)) list.Add(t);
        _config.Settings.WifiTargets = list.Take(8).ToList();
        _config.Save();
    }

    public string Connect(string addr)
    {
        var r = _adb.Connect(addr);
        if (!r.Contains("connected", StringComparison.OrdinalIgnoreCase)) return r;
        var normalized = addr.Contains(':') ? addr : addr + ":5555";
        AddWifiTarget(normalized);
        return r;
    }
}
