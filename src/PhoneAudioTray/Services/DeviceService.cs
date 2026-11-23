using System.IO;
using System.Text.RegularExpressions;
using PhoneAudioTray.Infrastructure;
using PhoneAudioTray.Models;

namespace PhoneAudioTray.Services;

/// <summary>
/// 设备管理：列表、别名、连接方式判定、记忆地址、mDNS 去重、无效清理。
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

    public void SaveNames()
    {
        try { JsonStore.Write(_namesPath, _names); }
        catch (Exception ex) { Logger.Log($"save names failed: {ex.Message}"); }
    }

    public IReadOnlyList<AdbClient.DeviceInfo> Refresh() => _adb.ListDevices();

    /// <summary>刷新并合并重复记录：同一机型同时有 IP 形式与 mDNS 形式时只保留 IP 一条。</summary>
    public IReadOnlyList<AdbClient.DeviceInfo> RefreshMerged() => MergeDuplicates(_adb.ListDevices());

    public static string KindOf(string serial)
        => (serial.Contains(':') || serial.Contains("_adb-tls")) ? "WiFi" : "USB";

    private static readonly Regex IpPort = new(@"^\d+\.\d+\.\d+\.\d+:\d+$", RegexOptions.Compiled);

    /// <summary>同机型的 mDNS 记录与 IP 记录并存时，去掉 mDNS 那条（spec「同一设备两条记录」）。</summary>
    public static IReadOnlyList<AdbClient.DeviceInfo> MergeDuplicates(IEnumerable<AdbClient.DeviceInfo> devs)
    {
        var list = devs.ToList();
        var ipModels = list.Where(d => IpPort.IsMatch(d.Serial))
            .Select(d => d.Model)
            .Where(m => !string.IsNullOrEmpty(m))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list
            .Where(d => IpPort.IsMatch(d.Serial) || string.IsNullOrEmpty(d.Model) || !ipModels.Contains(d.Model))
            .ToList();
    }

    public string NameOf(AdbClient.DeviceInfo d)
    {
        if (_names.TryGetValue(d.Serial, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (!string.IsNullOrEmpty(d.Model) && _names.TryGetValue(d.Model, out n) && !string.IsNullOrWhiteSpace(n)) return n;
        return string.IsNullOrEmpty(d.Model) ? d.Serial : d.Model;
    }

    public void Rename(string serial, string name)
    {
        _names[serial] = name.Trim(); // 留空 = 恢复默认（NameOf 会跳过空白）
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

    /// <summary>连接地址；成功则记入 WifiTargets（新的排最前，最多 8 条）。</summary>
    public string Connect(string addr)
    {
        var r = _adb.Connect(addr);
        if (!r.Contains("connected", StringComparison.OrdinalIgnoreCase)) return r;
        var normalized = addr.Contains(':') ? addr : addr + ":5555";
        AddWifiTarget(normalized);
        return r;
    }

    /// <summary>依次连接已记住的无线地址，返回连上的。</summary>
    public List<string> ConnectSaved()
    {
        var ok = new List<string>();
        foreach (var t in _config.Settings.WifiTargets)
        {
            var r = _adb.Connect(t);
            if (r.Contains("connected", StringComparison.OrdinalIgnoreCase)) ok.Add(t);
        }
        return ok;
    }

    /// <summary>清理无效连接：断开 offline/unauthorized 记录与「有 IP 同伴」的 mDNS 记录。
    /// 返回断开数量。唯一可用的连接不会被断开。</summary>
    public int ClearBad()
    {
        var all = _adb.ListDevicesAll();
        var good = all.Where(d => d.State == "device").ToList();
        var ipModels = good.Where(d => IpPort.IsMatch(d.Serial))
            .Select(d => d.Model).Where(m => !string.IsNullOrEmpty(m))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var d in all)
        {
            var bad = d.State != "device" ||
                      (d.Serial.Contains("_adb-tls") && !string.IsNullOrEmpty(d.Model) && ipModels.Contains(d.Model));
            if (!bad) continue;
            _adb.Disconnect(d.Serial);
            count++;
        }
        return count;
    }
}
