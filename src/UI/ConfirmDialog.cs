using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>テーマの色に合わせた はい／いいえ の確認ダイアログ（M15。Windows 標準の MessageBox は色を変えられないため）。既定は「いいえ」。</summary>
public static class ConfirmDialog
{
    public static bool Ask(Window? owner, string theme, string message)
    {
        var w = new Window
        {
            Title = AppInfo.Name,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Meiryo UI"),
            FontSize = 14,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner,
        };
        w.SetResourceReference(Control.BackgroundProperty, "WindowBg");
        w.SetResourceReference(Control.ForegroundProperty, "WindowFg");
        var yes = new Button { Content = Loc.Get("confirm.yes"), Width = 90, Padding = new Thickness(0, 5, 0, 5) };
        var no = new Button { Content = Loc.Get("confirm.no"), Width = 90, Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, IsCancel = true };
        yes.Click += (_, _) => w.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(buttons);
        w.Content = root;
        ThemeManager.Attach(w, theme);
        no.Loaded += (_, _) => no.Focus();
        return w.ShowDialog() == true;
    }
}
