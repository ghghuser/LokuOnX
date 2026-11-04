using System.Windows;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>メイン画面のうち、設定（デバイス・形式・保存先・分割間隔）と表示。録音の操作は MainWindow.Recording.cs。</summary>
public partial class MainWindow : Window
{
    private readonly PlayerPanel Player = new();   // 一覧（FileListPanel）の下に Footer として入れる
    private readonly Settings _settings;
    private readonly IAudioDeviceProvider _provider = new WasapiDeviceProvider();
    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private List<AudioDeviceInfo> _devices = new();
    private string _signature = "";
    private bool _loading;

    public MainWindow(Settings settings)
    {
        InitializeComponent();
        Files.Footer = Player;
        // 画面が小さいパソコンでは、作業領域に収まる高さにする（M6で一覧を足したため既定の高さが大きい）
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        _settings = settings;
        Title = settings.ReadOnly ? $"{AppInfo.Name}（2つ目の起動・設定は保存されません）" : AppInfo.Name;

        _loading = true;
        FolderBox.Text = settings.Folder;
        SplitBox.Text = SplitTime.Format(settings.SplitSeconds);
        LoadDevices();
        Devices.Init(settings, _devices, DefaultCaptureKey());
        Devices.Changed += () => { Devices.ClearError(); UpdateInfo(); };
        Devices.RefreshRequested += () => Refresh_Click(this, new RoutedEventArgs());
        Devices.GainChanged += (index, db) => _session?.SetGain(index, db);
        Quality.Init(settings);
        Quality.Changed += UpdateInfo;
        Detail.Init(settings, () => Devices.NameLabel);
        Detail.Changed += UpdateInfo;
        Presets.Init(settings);
        Presets.Prepare = CommitInputs;
        Presets.Acting += ClearErrors;
        Presets.Error += ShowError;
        Presets.Applied += ReloadFromSettings;
        if (settings.RecentFolders.Count == 0) settings.AddRecentFolder(settings.Folder);
        Files.Init(settings, AppPaths.Ffmpeg);
        Player.Init(AppPaths.Ffmpeg);
        Files.Selected += Player.Load;
        Files.PlayRequested += Player.PlayFile;
        Files.Releasing += Player.Release;
        ThemeManager.Attach(this, settings.Theme);
        _loading = false;

        // デバイスの抜き差し・既定の変更を、録音していないときは自動で一覧へ反映する
        _deviceTimer.Tick += (_, _) => PollDevices();
        _deviceTimer.Start();

        UpdateInfo();
        StartTimer();
        Loaded += async (_, _) =>
        {
            Files.Refresh();
            await RecoverAsync();
            Files.Refresh();   // 復旧で名前が付いたファイルも出す
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        Player.Shutdown();
        base.OnClosed(e);
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

    /// <summary>入力途中の欄（保存先・自動分割・パターン・番号・タグ）を設定へ反映する（録音開始・プリセット保存の直前）。</summary>
    private void CommitInputs()
    {
        FolderBox_LostFocus(this, new RoutedEventArgs());
        Split_LostFocus(this, new RoutedEventArgs());
        Detail.CommitAll();
    }

    /// <summary>プリセットを読み込んだあと、画面の各欄を設定の値で読み直す。</summary>
    private void ReloadFromSettings()
    {
        _loading = true;
        SplitBox.Text = SplitTime.Format(_settings.SplitSeconds);
        Quality.Reload();
        Detail.Reload();
        _loading = false;
        Devices.Rebuild(DefaultCaptureKey());
        Presets.Reload(null);
        UpdateInfo();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(_settings) { Owner = this }.ShowDialog();

    // ---- 設定の変更 ----
    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = FolderBox.Text.Trim();
        if (text.Length == 0) { FolderBox.Text = _settings.Folder; return; }
        var changed = !string.Equals(_settings.Folder, text, StringComparison.Ordinal);
        _settings.Folder = text;
        UpdateInfo();
        if (changed && Directory.Exists(text)) AddRecentFolder(text);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _settings.Folder };
        if (dlg.ShowDialog(this) != true) return;
        FolderBox.Text = dlg.FolderName;
        _settings.Folder = dlg.FolderName;
        UpdateInfo();
        AddRecentFolder(dlg.FolderName);
    }

    /// <summary>最近使った保存先（10件）へ入れて、一覧のプルダウンを更新する。</summary>
    private void AddRecentFolder(string folder)
    {
        _settings.AddRecentFolder(folder);
        Files.ReloadSources();
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
