using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>右上の歯車で開く、めったに使わない設定（画面の色・スリープ防止）とヘルプ・バージョン情報。値は Settings に即保存。</summary>
public partial class SettingsWindow : Window
{
    private sealed record ThemeChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ThemeChoice[] Themes =
    {
        new("auto", "自動（Windowsに合わせる）"), new("light", "ライト"), new("dark", "ダーク"),
    };

    private readonly Settings _s;
    private readonly bool _loading = true;

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _s = settings;
        ThemeBox.ItemsSource = Themes;
        ThemeBox.SelectedValue = settings.Theme;
        SleepCheck.IsChecked = settings.PreventSleep;
        VersionText.Text = $"{AppInfo.Name} {AppInfo.Version}";
        ThemeManager.Attach(this, settings.Theme);
        _loading = false;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedValue is not string key) return;
        _s.Theme = key;
        ThemeManager.Apply(key);
    }

    private void Sleep_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.PreventSleep = SleepCheck.IsChecked == true;
    }

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { }
        e.Handled = true;
    }
}
