using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// 封装 adb 命令。所有调用带序列号，支持多设备。
/// </summary>
public sealed class AdbClient
{
    private readonly string _adb;
    private readonly string _scrcpy;

    public AdbClient(string scrcpyPath)
    {
        _scrcpy = scrcpyPath;
        _adb = Path.Combine(Path.GetDirectoryName(scrcpyPath) ?? "", "adb.exe");
    }

    public string AdbPath => _adb;
    public string ScrcpyPath => _scrcpy;

    /// <summary>按 v1 规则定位 scrcpy.exe：同目录 → PATH → winget。</summary>
    public static string? FindScrcpy(string appDir)
    {
        var local = Path.Combine(appDir, "scrcpy", "scrcpy.exe");
        if (File.Exists(local)) return local;

        var fromPath = FindInPath("scrcpy.exe");
        if (fromPath is not null) return fromPath;

        var pkgRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(pkgRoot))
        {
            foreach (var d in Directory.EnumerateDirectories(pkgRoot, "Genymobile.scrcpy_*"))
                foreach (var s in Directory.EnumerateDirectories(d, "scrcpy-win64-*"))
                {
                    var exe = Path.Combine(s, "scrcpy.exe");
                    if (File.Exists(exe)) return exe;
                }
        }
        return null;
    }

    private static string? FindInPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var full = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    private (int code, string output) Run(string args, int timeoutMs = 8000)
    {
        try
        {
            var psi = new ProcessStartInfo(_adb, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { p.Kill(); return (-1, ""); }
            var o = outTask.Result + errTask.Result;
            return (p.ExitCode, o);
        }
        catch
        {
            return (-1, "");
        }
    }

    private string Shell(string serial, string cmd, int timeoutMs = 8000)
        => Run($"-s {serial} shell {cmd}", timeoutMs).output;

    /// <summary>执行任意 shell 命令（解锁手势、批量按键等）。</summary>
    public string ShellRaw(string serial, string cmd, int timeoutMs = 8000)
        => Shell(serial, cmd, timeoutMs);

    public record DeviceInfo(string Serial, string Model);

    public IReadOnlyList<DeviceInfo> ListDevices()
    {
        var (_, o) = Run("devices -l");
        var list = new List<DeviceInfo>();
        foreach (var line in o.Split('\n'))
        {
            if (!Regex.IsMatch(line, @"\sdevice(\s|$)")) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var model = Regex.Match(line, @"model:(\S+)").Groups[1].Value;
            list.Add(new DeviceInfo(parts[0], model));
        }
        return list;
    }

    public string Connect(string address)
    {
        if (!Regex.IsMatch(address, @":\d+$")) address += ":5555";
        return Run($"connect {address}").output.Trim();
    }

    public void Disconnect(string serial) => Run($"disconnect {serial}");

    public string Pair(string address, string code)
    {
        if (!Regex.IsMatch(address, @":\d+$")) address += ":5555";
        return Run($"pair {address} {code}").output.Trim();
    }

    /// <summary>列出 mDNS 自动发现到的无线调试地址（_adb-tls-connect._tcp）。</summary>
    public List<string> ListMdns()
    {
        var (_, o) = Run("mdns services");
        var addrs = new List<string>();
        foreach (var line in o.Split('\n'))
        {
            if (!line.Contains("_adb-tls-connect._tcp")) continue;
            var m = Regex.Match(line, @"(\d+\.\d+\.\d+\.\d+:\d+)");
            if (m.Success) addrs.Add(m.Groups[1].Value);
        }
        return addrs.Distinct().ToList();
    }

    public record DeviceStateInfo(string Serial, string State, string Model);

    /// <summary>全部 adb 记录（含 offline/unauthorized），供无效连接清理使用。</summary>
    public IReadOnlyList<DeviceStateInfo> ListDevicesAll()
    {
        var (_, o) = Run("devices -l");
        var list = new List<DeviceStateInfo>();
        foreach (var line in o.Split('\n'))
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "List" || parts[1] == "devices") continue;
            var model = Regex.Match(line, @"model:(\S+)").Groups[1].Value;
            list.Add(new DeviceStateInfo(parts[0], parts[1], model));
        }
        return list;
    }

    /// <summary>读 screen_off_timeout 原值。读不到返回 -1。</summary>
    public int GetScreenTimeout(string serial)
    {
        var o = Shell(serial, "settings get system screen_off_timeout");
        return int.TryParse(o.Trim(), out var v) ? v : -1;
    }

    public void SetScreenTimeout(string serial, int ms)
        => Shell(serial, $"settings put system screen_off_timeout {ms}");

    /// <summary>物理屏分辨率。读不到返回空数组，由调用方回落到虚拟屏或默认比例。</summary>
    public int[] GetScreenSize(string serial)
    {
        var o = Shell(serial, "wm size");
        var m = Regex.Match(o, @"(\d+)x(\d+)");
        if (m.Success) return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) };
        return Array.Empty<int>();
    }

    /// <summary>读 scrcpy 虚拟屏宽高（= 视频流真实方向），App 横竖屏时会变。</summary>
    public int[] GetVideoSize(string serial)
    {
        var o = Shell(serial, "dumpsys display");
        foreach (var line in o.Split('\n'))
        {
            if (!line.Contains("DisplayDeviceInfo{\"scrcpy\"")) continue;
            var m = Regex.Match(line, @",\s*(\d{3,5})\s+x\s+(\d{3,5}),");
            if (m.Success)
                return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) };
        }
        return Array.Empty<int>();
    }

    public bool HasMediaSession(string serial)
    {
        var o = Shell(serial, "dumpsys media_session");
        return Regex.IsMatch(o, @"package=");
    }

    public void SendKey(string serial, int keyCode) => Shell(serial, $"input keyevent {keyCode}");

    public void DismissKeyguard(string serial) => Shell(serial, "wm dismiss-keyguard");

    /// <summary>
    /// 设备状态结构化判定。必须合并两条命令：isKeyguardShowing 在 dumpsys window 里，
    /// interactiveState/screenState 在 dumpsys window policy 里——实测 policy 输出无
    /// isKeyguardShowing，只跑一条会恒判未锁（对齐 v1 的合并查询）。
    /// </summary>
    public record ScreenState(bool On, bool Locked, bool Valid);

    public ScreenState GetScreenState(string serial)
    {
        var txt = Shell(serial, "dumpsys window policy");
        txt += "\n" + Shell(serial, "dumpsys window");

        bool awake = txt.Contains("interactiveState=INTERACTIVE_STATE_AWAKE");
        bool asleep = txt.Contains("interactiveState=INTERACTIVE_STATE_SLEEP") ||
                      txt.Contains("interactiveState=INTERACTIVE_STATE_DOZING");
        if (!awake && !asleep)
        {
            awake = txt.Contains("screenState=SCREEN_STATE_ON");
            asleep = txt.Contains("screenState=SCREEN_STATE_OFF");
        }
        bool locked = txt.Contains("isKeyguardShowing=true") || txt.Contains("mShowingLockscreen=true");
        return new ScreenState(awake && !asleep, locked, !string.IsNullOrWhiteSpace(txt));
    }
}
