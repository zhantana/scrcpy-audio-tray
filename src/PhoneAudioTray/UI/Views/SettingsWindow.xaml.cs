using System.Windows;
using System.Windows.Controls;
using PhoneAudioTray.Models;
using PhoneAudioTray.Services;

namespace PhoneAudioTray.UI.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;
    private readonly Action _onSaved;

    public SettingsWindow(ConfigService config, Action onSaved)
    {
        InitializeComponent();
        _config = config;
        _onSaved = onSaved;
        LoadValues();
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            { box.SelectedItem = item; return; }
    }

    private void LoadValues()
    {
        var s = _config.Settings;
        SelectByTag(CmbBitrate, s.VideoBitRate.ToString());
        SelectByTag(CmbCodec, s.VideoCodec);
        SelectByTag(CmbFps, s.MaxFps.ToString());
        SelectByTag(CmbSize, s.MaxSize.ToString());
        SelectByTag(CmbBuffer, s.Buffer.ToString());
        SelectByTag(CmbAudioCodec, s.Codec);
    }

    private static string TagOf(System.Windows.Controls.ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _config.Settings;
        s.VideoBitRate = int.TryParse(TagOf(CmbBitrate), out var br) ? br : 16000000;
        var vc = TagOf(CmbCodec);
        s.VideoCodec = vc is "h265" or "av1" ? vc : "h264";
        s.MaxFps = int.TryParse(TagOf(CmbFps), out var fps) ? fps : 60;
        s.MaxSize = int.TryParse(TagOf(CmbSize), out var sz) ? sz : 0;
        s.Buffer = int.TryParse(TagOf(CmbBuffer), out var buf) ? buf : 40;
        s.Codec = TagOf(CmbAudioCodec) == "opus" ? "opus" : "raw";
        _config.Save();
        _onSaved(); // 重启投屏让设置生效
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void CmbBitrate_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
}
