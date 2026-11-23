using System.IO;
using System.Text.Json;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// 简单的 JSON 文件读写，忽略大小写。写入先落 .tmp 再原子替换，避免断电产生半截文件；
/// 读取遇到损坏文件时改名留档并上报，不静默回落默认。
/// </summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>读取到损坏文件时触发，参数为文件路径。</summary>
    public static event Action<string>? Corrupted;

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        catch (Exception ex)
        {
            // 文件存在还读不出来 = 损坏：改名留档，不覆盖原文件
            var corruptPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Move(path, corruptPath); } catch { }
            Logger.Log($"config corrupted: {path} -> {corruptPath} ({ex.Message})");
            Corrupted?.Invoke(path);
            return null;
        }
    }

    /// <summary>原子写入。失败会抛异常，由调用方决定怎么处理（记日志/提示）。</summary>
    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
