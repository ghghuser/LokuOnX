using System.Windows;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>メイン画面のうち、設定（デバイス・形式・保存先・分割間隔）と表示。録音の操作は MainWindow.Recording.cs。</summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly IAudioDeviceProvider _provider = new WasapiDeviceProvider();
    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private List<AudioDeviceInfo> _devices = new();
    private string _signature = "";
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
        SplitBox.Text = SplitTime.Format(settings.SplitSeconds);
        LoadDevices();
        Devices.Init(settings, _devices, DefaultCaptureKey());
        Devices.Changed += () => { Devices.ClearError(); UpdateInfo(); };
        Devices.GainChanged += (index, db) => _session?.SetGain(index, db);
        Quality.Init(settings);
        Quality.Changed += UpdateInfo;
        Detail.Init(settings, () => Devices.NameLabel);
        Detail.Changed += UpdateInfo;
        _loading = false;

        // デバイスの抜き差し・既定の変更を、録音していないときは自動で一覧へ反映する
        _deviceTimer.Tick += (_, _) => PollDevices();
        _deviceTimer.Start();

        UpdateInfo();
        StartTimer();
        Loaded += async (_, _) => await RecoverAsync();
    }

    private void LoadDevices()
    {
        try
        {
            _devices = _provider.List().ToList();
            _signature = _provider.Signature();
        }
        catch (Exception ex)
        {
            _devices = new List<AudioDeviceInfo>();
            StatusText.Text = "デバイス一覧を取得できませんでした: " + ex.Message;
        }
    }

    private string? DefaultCaptureKey()
    {
        try { return _provider.DefaultCaptureKey(); }
        catch (Exception) { return null; }
    }

    /// <summary>3秒ごと。つながっているデバイスや既定が変わっていたら一覧を読み直す（録音中は何もしない）。</summary>
    private void PollDevices()
    {
        if (_session != null) return;
        try
        {
            if (_provider.Signature() == _signature) return;
            LoadDevices();
            Devices.Reload(_devices);
        }
        catch (Exception) { }
    }

    /// <summary>設定が変わったとき、常時表示の「名前の例」などを更新する。</summary>
    private void UpdateInfo()
    {
        Detail.Refresh();
        Quality.Refresh();
    }

    // ---- 設定の変更 ----
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
        LoadDevices();
        Devices.Reload(_devices);
    }

    private void Split_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SplitTime.TryParse(SplitBox.Text, out var sec)) _settings.SplitSeconds = sec;
        SplitBox.Text = SplitTime.Format(_settings.SplitSeconds);
    }
}
