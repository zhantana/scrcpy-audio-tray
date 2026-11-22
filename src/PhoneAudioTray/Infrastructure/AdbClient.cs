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

    public int[] GetScreenSize(string serial)
    {
        var o = Shell(serial, "wm size");
        var m = Regex.Match(o, @"(\d+)x(\d+)");
        if (m.Success) return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) };
        return new[] { 1080, 2400 };
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

    public string GetDeviceState(string serial) => Shell(serial, "dumpsys window policy");
}
