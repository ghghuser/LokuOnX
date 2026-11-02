using System.Windows;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>メイン画面のうち、設定（デバイス・形式・保存先・分割間隔）と表示。録音の操作は MainWindow.Recording.cs。</summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly IAudioDeviceProvider _provider = new WasapiDeviceProvider();
    private List<AudioDeviceInfo> _devices = new();
    private bool _loading;

    public MainWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        Title = settings.ReadOnly ? $"{AppInfo.Name}（2つ目の起動・設定は保存されません）" : AppInfo.Name;

        _loading = true;
        FormatBox.ItemsSource = OutputFormat.All;
        FormatBox.SelectedValue = settings.Format;
        FolderBox.Text = settings.Folder;
        SplitBox.Text = settings.SplitMinutes.ToString();
        RefreshDevices();
        Quality.Init(settings);
        Quality.Changed += UpdateInfo;
        Detail.Init(settings, () => SelectedDevice?.Label ?? "");
        Detail.Changed += UpdateInfo;
        _loading = false;

        UpdateInfo();
        StartTimer();
        Loaded += async (_, _) => await RecoverAsync();
    }

    private AudioDeviceInfo? SelectedDevice => DeviceBox.SelectedItem as AudioDeviceInfo;

    private void RefreshDevices()
    {
        var wanted = SelectedDevice?.Key ?? _settings.Device;
        try { _devices = _provider.List().ToList(); }
        catch (Exception ex)
        {
            _devices = new List<AudioDeviceInfo>();
            StatusText.Text = "デバイス一覧を取得できませんでした: " + ex.Message;
        }
        DeviceBox.ItemsSource = _devices;
        // 前回選んだデバイス → なければ（初回など）Windowsの既定の入力デバイス → それも無ければ先頭のマイク
        var defaultKey = _provider.DefaultCaptureKey();
        DeviceBox.SelectedItem = _devices.FirstOrDefault(d => d.Key == wanted)
                                 ?? _devices.FirstOrDefault(d => d.Key == defaultKey)
                                 ?? _devices.FirstOrDefault(d => !d.IsLoopback);
    }

    /// <summary>設定が変わったとき、常時表示の「名前の例」などを更新する。</summary>
    private void UpdateInfo()
    {
        Detail.Refresh();
        Quality.Refresh();
    }

    // ---- 設定の変更 ----
    private void DeviceBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SelectedDevice != null) _settings.Device = SelectedDevice.Key;
        UpdateInfo();
    }

    private void FormatBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || FormatBox.SelectedValue is not string f) return;
        _settings.Format = f;
        UpdateInfo();
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = FolderBox.Text.Trim();
        if (text.Length == 0) { FolderBox.Text = _settings.Folder; return; }
        _settings.Folder = text;
        UpdateInfo();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _settings.Folder };
        if (dlg.ShowDialog(this) != true) return;
        FolderBox.Text = dlg.FolderName;
        _settings.Folder = dlg.FolderName;
        UpdateInfo();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        RefreshDevices();
        _loading = false;
        UpdateInfo();
    }

    private void Split_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(SplitBox.Text.Trim(), out var n) && n >= 0 && n <= 1440) _settings.SplitMinutes = n;
        SplitBox.Text = _settings.SplitMinutes.ToString();
    }
}
