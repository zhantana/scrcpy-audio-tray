using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.Services;

/// <summary>
/// 手机端媒体暂停。发 KEYCODE_MEDIA_PLAY_PAUSE，先确认有活跃媒体会话才发，
/// 避免误拉起手机自带音乐 App。游戏不响应媒体键——这是 Android 侧限制。
/// </summary>
public sealed class MediaService
{
    private readonly AdbClient _adb;

    public MediaService(AdbClient adb) => _adb = adb;

    public record Result(bool Ok, string Message);

    public Result TogglePause(string serial)
    {
        if (string.IsNullOrEmpty(serial))
            return new Result(false, "没有选中的设备");

        if (!_adb.HasMediaSession(serial))
            return new Result(false, "手机上没有正在播放的媒体");

        _adb.SendKey(serial, 85); // KEYCODE_MEDIA_PLAY_PAUSE
        Logger.Log($"media pause sent to {serial}");
        return new Result(true, "已发送暂停 / 继续");
    }
}
