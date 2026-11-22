using System.Windows;
using System.Windows.Input;
using PhoneAudioTray.Infrastructure;

namespace PhoneAudioTray.UI.Views;

public partial class HotkeyCaptureWindow : Window
{
    /// <summary>录制结果。null = 取消。</summary>
    public string? Result { get; private set; }
    private readonly string _purpose;

    public HotkeyCaptureWindow(string purpose, string current)
    {
        InitializeComponent();
        _purpose = purpose;
        LblTitle.Text = $"设置{purpose}快捷键";
        LblCurrent.Text = string.IsNullOrEmpty(current) ? "当前：未设置" : $"当前：{current}";
        PreviewKeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = null;
            Close();
            e.Handled = true;
            return;
        }

        // 修饰键本身不算，等真正的键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        if (vk == 0)
        {
            LblKey.Text = "这个键不行，换一个";
            e.Handled = true;
            return;
        }

        // 拼成 Ctrl+Alt+P 形式
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(e.Key.ToString());

        var spec = string.Join("+", parts);
        // 校验能被解析
        if (!HotkeyParser.TryParse(spec, out _, out _))
        {
            LblKey.Text = "这个键认不出来，换一个";
            e.Handled = true;
            return;
        }

        Result = spec;
        LblKey.Text = spec;
        e.Handled = true;
        Close();
    }
}
