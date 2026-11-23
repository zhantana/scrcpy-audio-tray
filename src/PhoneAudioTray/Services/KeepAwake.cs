using System.IO;
using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 投屏期间不自动息屏：把 screen_off_timeout 原值备份到 device-timeout.json 后设为
/// 2147483647，停止投屏时还原。启动时清理上次异常退出留下的残留，避免把「永不息屏」
/// 当成用户原始设置记下来。只依赖 screen_off_timeout——svc power stayon 只在充电时
/// 生效，还会覆盖用户自己的充电保持唤醒设置，不得使用。
/// </summary>
public sealed class KeepAwake
{
    private const int Never = 2147483647;
    private readonly AdbClient _adb;
    private readonly string _backupPath;

    public KeepAwake(AdbClient adb)
    {
        _adb = adb;
        _backupPath = Path.Combine(AppContext.BaseDirectory, "device-timeout.json");
    }

    /// <summary>
    /// 清理上次异常退出可能留下的「永不息屏」残留。备份文件还在且当前值确为
    /// 永不息屏才还原；用户中途自己改过值则不动。转发启动前调用。
    /// </summary>
    public void ClearStale(string serial)
    {
        var saved = JsonStore.Read<int[]>(_backupPath);
        if (saved is null || saved.Length != 2) return;
        try
        {
            if (saved[0] > 0 && _adb.GetScreenTimeout(serial) == Never)
                _adb.SetScreenTimeout(serial, saved[0]);
        }
        catch (Exception ex) { Logger.Log($"clear stale timeout failed: {ex.Message}"); }
        TryDeleteBackup();
    }

    /// <summary>投屏启动前调用：备份原值并设为永不息屏。已是永不息屏（用户自己的设置）则不备份不动。</summary>
    public void Push(string serial)
    {
        try
        {
            var cur = _adb.GetScreenTimeout(serial);
            if (cur == Never || cur <= 0) return;
            JsonStore.Write(_backupPath, new[] { cur, 0 });
            _adb.SetScreenTimeout(serial, Never);
            Logger.Log($"keepawake push: {serial} original={cur}ms");
        }
        catch (Exception ex) { Logger.Log($"keepawake push failed: {ex.Message}"); }
    }

    /// <summary>停止投屏 / 退出时调用：还原原值并删除备份。</summary>
    public void Pop(string serial)
    {
        var saved = JsonStore.Read<int[]>(_backupPath);
        if (saved is null || saved.Length != 2) return;
        try
        {
            if (saved[0] > 0) _adb.SetScreenTimeout(serial, saved[0]);
            Logger.Log($"keepawake pop: {serial} restore={saved[0]}ms");
        }
        catch (Exception ex) { Logger.Log($"keepawake pop failed: {ex.Message}"); }
        TryDeleteBackup();
    }

    private void TryDeleteBackup()
    {
        try { if (File.Exists(_backupPath)) File.Delete(_backupPath); }
        catch (Exception ex) { Logger.Log($"delete timeout backup failed: {ex.Message}"); }
    }
}
