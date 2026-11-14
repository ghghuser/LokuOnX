using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 右上の歯車で開く設定。タブは「詳細設定」（画面の色・スリープ防止・デバイスが外れたとき・タスクトレイ・ホットキー）／「ヘルプ」／「バージョン情報」。値は Settings に即保存。
/// トレイの表示とホットキーの登録はメイン画面側の処理なので、コールバックで反映する。
/// </summary>
public partial class SettingsWindow : Window
{
    private sealed record ThemeChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static ThemeChoice[] Themes() => new ThemeChoice[]
    {
        new("auto", Loc.Get("theme.auto")), new("light", Loc.Get("theme.light")), new("dark", Loc.Get("theme.dark")),
        new("soft", Loc.Get("theme.soft")), new("softdark", Loc.Get("theme.softdark")),
    };

    private readonly Settings _s;
    private readonly Func<(string Text, bool Error)> _applyHotkeys;
    private readonly Action<bool> _hotkeyCapture;
    private readonly Action _trayChanged;
    private readonly Action _asioChanged;
    private readonly bool _loading = true;

    /// <param name="applyHotkeys">ホットキーを登録し直し、結果のメッセージ（登録できなかったキー）を返す。</param>
    /// <param name="hotkeyCapture">キーの割り当て中は true（登録済みのキーを一時的に外すため）。</param>
    /// <param name="trayChanged">トレイの設定が変わった。</param>
    /// <param name="asioChanged">ASIOを一覧に出す設定が変わった（メイン画面がデバイス一覧を読み直す）。</param>
    public SettingsWindow(Settings settings, Func<(string Text, bool Error)> applyHotkeys,
        Action<bool> hotkeyCapture, Action trayChanged, Action asioChanged)
    {
        InitializeComponent();
        _s = settings;
        _applyHotkeys = applyHotkeys;
        _hotkeyCapture = hotkeyCapture;
        _trayChanged = trayChanged;
        _asioChanged = asioChanged;
        AsioCheck.IsChecked = settings.AsioEnabled;
        var langs = LangList.Scan(Path.Combine(AppPaths.Dir, "Lang"), new[] { "ja" }, c => App.ReadEmbeddedLang(c + ".ini"));
        if (!langs.Any(l => l.Code.Equals(settings.Language, StringComparison.OrdinalIgnoreCase))) langs.Add(new LangInfo(settings.Language, settings.Language));
        LangBox.ItemsSource = langs;
        LangBox.SelectedValue = langs.First(l => l.Code.Equals(settings.Language, StringComparison.OrdinalIgnoreCase)).Code;
        ThemeBox.ItemsSource = Themes();
        BuildHelp();
        LoadAboutIcon();
        ThemeBox.SelectedValue = settings.Theme;
        SleepCheck.IsChecked = settings.PreventSleep;
        TrayCheck.IsChecked = settings.ShowTray;
        MinTrayCheck.IsChecked = settings.MinimizeToTray;
        MinTrayCheck.IsEnabled = settings.ShowTray;
        WaitCheck.IsChecked = settings.WaitForDevice;
        SplitReturnCheck.IsChecked = settings.SplitOnReturn;
        VersionText.Text = $"{AppInfo.Name} {AppInfo.DisplayVersion}";
        Hotkeys.Init(settings);
        Hotkeys.Changed += () =>
        {
            var (text, error) = _applyHotkeys();
            Hotkeys.SetMessage(text, error);
        };
        Hotkeys.CaptureChanged += capturing => _hotkeyCapture(capturing);
        ThemeManager.Attach(this, settings.Theme);
        _loading = false;
        if (settings.HotkeysOn)
        {
            var (text, error) = _applyHotkeys();   // 前に登録できなかったキーがあれば、ここにも出す
            Hotkeys.SetMessage(text, error);
        }
    }

    /// <summary>ヘルプ: グループごとに見出し（help.gN.title）と項目（help.gN.1, 2, …）を並べる。項目の頭の印は黄色（チェックなどと同じ色）。</summary>
    private void BuildHelp()
    {
        for (var g = 1; Loc.Has($"help.g{g}.title"); g++)
        {
            var head = new TextBlock { Text = Loc.Get($"help.g{g}.title"), Margin = new Thickness(0, g == 1 ? 4 : 14, 0, 6) };
            head.SetResourceReference(StyleProperty, "SectionTitle");
            HelpPanel.Children.Add(head);
            for (var n = 1; Loc.Has($"help.g{g}.{n}"); n++)
            {
                var row = new Grid { Margin = new Thickness(2, 0, 0, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var mark = new TextBlock { Text = "🙹", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
                mark.SetResourceReference(TextBlock.ForegroundProperty, "ToggleOn");
                var text = new TextBlock
                {
                    Text = Loc.Get($"help.g{g}.{n}"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Light,
                    LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Opacity = 0.95,
                };
                Grid.SetColumn(text, 1);
                row.Children.Add(mark);
                row.Children.Add(text);
                HelpPanel.Children.Add(row);
            }
        }
    }

    /// <summary>バージョン情報の背景: アイコンの一番大きい絵（256px）を使う。読めなければ何も出さない。</summary>
    private void LoadAboutIcon()
    {
        try
        {
            var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/LokuOnX.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            AboutIcon.Source = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch (Exception) { }
    }

    private void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangBox.SelectedValue is not string code) return;
        _s.Language = code;
        LangNote.Visibility = Visibility.Visible;   // 反映は再起動後
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

    private void Device_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.WaitForDevice = WaitCheck.IsChecked == true;
        _s.SplitOnReturn = SplitReturnCheck.IsChecked == true;
    }

    private void Asio_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AsioEnabled = AsioCheck.IsChecked == true;
        _asioChanged();
    }

    private void Tray_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ShowTray = TrayCheck.IsChecked == true;
        _s.MinimizeToTray = MinTrayCheck.IsChecked == true;
        MinTrayCheck.IsEnabled = _s.ShowTray;   // アイコンが無いと、格納した画面を呼び戻せない
        _trayChanged();
    }

    /// <summary>settings.ini・Profiles・エラーの記録（lokuonx-crash.log）がある、exe と同じフォルダを開く。</summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Dir}\"") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { }
        e.Handled = true;
    }
}
