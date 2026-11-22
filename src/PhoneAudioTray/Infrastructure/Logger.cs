using System.IO;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// 写 tray-error.log，跟随程序目录。线程安全。
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "tray-error.log");

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
