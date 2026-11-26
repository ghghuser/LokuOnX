using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Recorder.Core;

namespace Gokko.UI;

/// <summary>設定・情報: 画面の色、バージョン、ライセンス、GitHub。LokuOnX のバージョン情報と同じ内容。</summary>
public sealed class AboutWindow : Window
{
    private const string Repo = "https://github.com/ghghuser/LokuOnX";

    public AboutWindow(string theme, Action<string> themeChanged)
    {
        Title = Loc.Get("g.about.title");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ThemeManager.Attach(this);

        var root = new StackPanel { Margin = new Thickness(28, 22, 28, 22), MinWidth = 380 };
        try
        {
            root.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/gokko-icon.png")),
                Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
            });
        }
        catch (Exception) { /* 画像が無くても表示する */ }
        root.Children.Add(new TextBlock { Text = "Gokko4DJ", FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(new TextBlock { Text = AppInfo.DisplayVersion, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 12) });
        root.Children.Add(new TextBlock { Text = Loc.Get("g.about.desc"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = Loc.Get("g.about.license"), TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12, Margin = new Thickness(0, 0, 0, 14) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = Loc.Get("g.about.theme"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var combo = new ComboBox { Width = 160 };
        foreach (var k in new[] { "g.about.theme.auto", "g.about.theme.light", "g.about.theme.dark" }) combo.Items.Add(Loc.Get(k));
        combo.SelectedIndex = theme == "light" ? 1 : theme == "dark" ? 2 : 0;
        combo.SelectionChanged += (_, _) => themeChanged(combo.SelectedIndex == 1 ? "light" : combo.SelectedIndex == 2 ? "dark" : "auto");
        row.Children.Add(combo);
        root.Children.Add(row);

        var links = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        links.Children.Add(LinkButton("g.about.github", Repo));
        links.Children.Add(LinkButton("g.about.licenselink", Repo + "/blob/main/LICENSE"));
        var folder = new Button { Content = Loc.Get("g.about.folder"), Margin = new Thickness(4) };
        folder.Click += (_, _) => Open(AppContext.BaseDirectory);
        links.Children.Add(folder);
        root.Children.Add(links);

        var ok = new Button { Content = Loc.Get("g.dlg.ok"), IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(ok);
        Content = root;
    }

    private Button LinkButton(string key, string url)
    {
        var b = new Button { Content = Loc.Get(key), Margin = new Thickness(4) };
        b.Click += (_, _) => Open(url);
        return b;
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { /* 開けなくても何もしない */ }
    }
}
