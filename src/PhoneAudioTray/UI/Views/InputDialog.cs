using System.Windows;
using Wpf = System.Windows.Controls;

namespace PhoneAudioTray.UI.Views;

/// <summary>简单输入对话框：一个或两个输入框（添加设备 / 配对 / 重命名共用）。纯代码构建，无 XAML。</summary>
public sealed class InputDialog : Window
{
    public bool Confirmed { get; private set; }
    public string Value1 { get; private set; } = "";
    public string Value2 { get; private set; } = "";

    /// <summary>显示对话框并返回 (是否确认, 输入1, 输入2)。</summary>
    public static (bool ok, string v1, string v2) Show(string title, string label1, string? label2 = null,
        string initial1 = "", string initial2 = "")
    {
        var dlg = new InputDialog(title, label1, label2, initial1, initial2);
        dlg.ShowDialog();
        return (dlg.Confirmed, dlg.Value1, dlg.Value2);
    }

    public InputDialog(string title, string label1, string? label2, string initial1, string initial2)
    {
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;

        var tb1 = new Wpf.TextBox { MinWidth = 280, Text = initial1 };
        var tb2 = label2 is null ? null : new Wpf.TextBox { MinWidth = 280, Text = initial2 };

        var grid = new Wpf.Grid { Margin = new Thickness(14) };
        var rows = 2 + (label2 is null ? 0 : 2);
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new Wpf.RowDefinition { Height = GridLength.Auto });

        var row = 0;
        grid.Children.Add(new Wpf.TextBlock { Text = label1, Margin = new Thickness(0, 0, 0, 4) });
        Wpf.Grid.SetRow(tb1, ++row);
        grid.Children.Add(tb1);
        if (tb2 is not null && label2 is not null)
        {
            grid.Children.Add(new Wpf.TextBlock { Text = label2, Margin = new Thickness(0, 10, 0, 4) });
            Wpf.Grid.SetRow(tb2, ++row);
            grid.Children.Add(tb2);
        }

        var buttons = new Wpf.StackPanel
        {
            Orientation = Wpf.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Wpf.Button { Content = "确定", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        var cancel = new Wpf.Button
        {
            Content = "取消",
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        };
        ok.Click += (_, _) =>
        {
            Confirmed = true;
            Value1 = tb1.Text.Trim();
            Value2 = tb2?.Text.Trim() ?? "";
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Wpf.Grid.SetRow(buttons, ++row);
        grid.Children.Add(buttons);

        Content = grid;
        tb1.Focus();
    }
}
