using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// テーマの色に合わせた確認・お知らせのダイアログ（M15。Windows 標準の MessageBox は色を変えられないため置き換え）。
/// 背景は少し明るい面（PopupBg）。Ask は はい／いいえ（既定は「いいえ」。yesDefault で「はい」）、Info は OK だけ。
/// 起動前・想定外の例外の表示だけは、確実に出すため標準の MessageBox のまま（App.xaml.cs）。
/// </summary>
public static class ConfirmDialog
{
    public static bool Ask(Window? owner, string message, bool yesDefault = false) => Show(owner, message, true, yesDefault);

    public static void Info(Window? owner, string message) => Show(owner, message, false, true);

    private static bool Show(Window? owner, string message, bool askYesNo, bool yesDefault)
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
        };
        if (owner != null && owner.IsVisible) w.Owner = owner;
        w.SetResourceReference(Control.BackgroundProperty, "PopupBg");
        w.SetResourceReference(Control.ForegroundProperty, "WindowFg");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button main;
        if (askYesNo)
        {
            var yes = new Button { Content = Loc.Get("confirm.yes"), Width = 90, Padding = new Thickness(0, 5, 0, 5), IsDefault = yesDefault };
            var no = new Button { Content = Loc.Get("confirm.no"), Width = 90, Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(8, 0, 0, 0), IsDefault = !yesDefault, IsCancel = true };
            yes.Click += (_, _) => w.DialogResult = true;
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            main = yesDefault ? yes : no;
        }
        else
        {
            main = new Button { Content = Loc.Get("confirm.ok"), Width = 90, Padding = new Thickness(0, 5, 0, 5), IsDefault = true, IsCancel = true };
            main.Click += (_, _) => w.DialogResult = true;
            buttons.Children.Add(main);
        }
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(buttons);
        w.Content = root;
        ThemeManager.Attach(w, ThemeManager.Mode);
        main.Loaded += (_, _) => main.Focus();
        return w.ShowDialog() == true;
    }
}
