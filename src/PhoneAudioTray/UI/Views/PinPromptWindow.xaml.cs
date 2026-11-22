using System.Windows;
using System.Windows.Input;

namespace PhoneAudioTray.UI.Views;

/// <summary>首次遇到锁屏密码时询问并保存（对齐 v1）。取消/留空返回 null。</summary>
public partial class PinPromptWindow : Window
{
    public string? Result { get; private set; }

    public PinPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PinBox.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Commit();

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PinBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    private void Commit()
    {
        Result = string.IsNullOrWhiteSpace(PinBox.Password) ? null : PinBox.Password.Trim();
        DialogResult = Result is not null;
    }
}
