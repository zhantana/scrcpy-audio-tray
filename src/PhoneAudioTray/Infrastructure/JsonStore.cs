using System.IO;
using System.Text.Json;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// 简单的 JSON 文件读写，忽略大小写，容错。
/// </summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
        }
        catch
        {
            // 写配置失败不应让程序崩溃
        }
    }
}
