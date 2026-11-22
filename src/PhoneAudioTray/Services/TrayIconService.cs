using System.IO;
using System.Drawing;
using System.Windows.Forms;
using WfMouseButtons = System.Windows.Forms.MouseButtons;

namespace PhoneAudioTray.Services;

/// <summary>
/// 托盘图标。只用 WinForms 显示图标和捕获鼠标；菜单由上层用 WPF ContextMenu 弹出，
/// 这样不依赖 WinForms 消息循环，在 WPF 的 Dispatcher 上稳定工作。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _idle;
    private readonly Icon _active;

    public event EventHandler<WfMouseButtons>? Clicked;
    public event EventHandler? DoubleClicked;

    public TrayIconService(string appDir)
    {
        _idle = LoadIcon(Path.Combine(appDir, "Resources", "tray-idle.ico"));
        _active = LoadIcon(Path.Combine(appDir, "Resources", "tray-active.ico"));
        _icon = new NotifyIcon
        {
            Icon = _idle,
            Text = "手机音频托盘",
            Visible = true,
        };
        _icon.MouseClick += (_, e) => Clicked?.Invoke(this, e.Button);
        _icon.MouseDoubleClick += (_, _) => DoubleClicked?.Invoke(this, EventArgs.Empty);
    }

    private static Icon LoadIcon(string path)
        => File.Exists(path) ? new Icon(path) : SystemIcons.Application;

    public void SetActive(bool active) => _icon.Icon = active ? _active : _idle;

    public void Notify(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(2500, title, text, ToolTipIcon.Info);
        }
        catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _idle.Dispose();
        _active.Dispose();
    }
}
