using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhoneAudioTray.Infrastructure;

/// <summary>
/// 隐藏窗口接收 WM_HOTKEY。用 WinForms NativeWindow，因为 RegisterHotKey 需要 hwnd。
/// </summary>
public sealed class HotkeyWindow : NativeWindow
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WmHotkey = 0x0312;

    public event EventHandler<int>? HotkeyPressed;

    public HotkeyWindow()
    {
        var cp = new CreateParams { Caption = "PhoneAudioTrayHotkey" };
        CreateHandle(cp);
    }

    public bool Register(int id, uint modifiers, uint key) => RegisterHotKey(Handle, id, modifiers, key);
    public void Unregister(int id) => UnregisterHotKey(Handle, id);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            var id = m.WParam.ToInt32();
            HotkeyPressed?.Invoke(this, id);
        }
        base.WndProc(ref m);
    }
}

/// <summary>热键字符串解析：Ctrl+Alt+P / F8 / Pause → 修饰符 + 虚拟键。</summary>
public static class HotkeyParser
{
    public static bool TryParse(string? spec, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var nonModifierCount = 0;
        foreach (var raw in spec.Split('+'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            var low = p.ToLowerInvariant();
            switch (low)
            {
                case "ctrl": case "control": modifiers |= 0x0002; continue;
                case "alt": modifiers |= 0x0001; continue;
                case "shift": modifiers |= 0x0004; continue;
                case "win": modifiers |= 0x0008; continue;
            }

            // 单个数字/字母转成标准键名；RegisterHotKey 只能绑一个主键，多键写法直接拒绝
            string name = p.Length == 1 ? (char.IsDigit(p[0]) ? "D" + p : p.ToUpperInvariant()) : p;
            if (!Enum.TryParse<Keys>(name, true, out var k)) return false;
            nonModifierCount++;
            if (nonModifierCount > 1) return false;
            key = (uint)k;
        }
        return key != 0;
    }
}
