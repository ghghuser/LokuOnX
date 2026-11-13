using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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

    private static readonly ThemeChoice[] Themes =
    {
        new("auto", "自動（Windowsに合わせる）"), new("light", "ライト"), new("dark", "ダーク"), new("soft", "ライト (ニューモーフィズム風)"), new("softdark", "ダーク (ニューモーフィズム風)"),
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
        ThemeBox.ItemsSource = Themes;
        ThemeBox.SelectedValue = settings.Theme;
        SleepCheck.IsChecked = settings.PreventSleep;
        TrayCheck.IsChecked = settings.ShowTray;
        MinTrayCheck.IsChecked = settings.MinimizeToTray;
        MinTrayCheck.IsEnabled = settings.ShowTray;
        WaitCheck.IsChecked = settings.WaitForDevice;
        SplitReturnCheck.IsChecked = settings.SplitOnReturn;
        VersionText.Text = $"{AppInfo.Name} {AppInfo.Version}";
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
