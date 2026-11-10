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
    private readonly IAudioDeviceProvider _provider;
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
        _provider = new AppDeviceProvider(() => settings.AsioEnabled);   // ASIOは設定でONのときだけ一覧に出る（M8）
        InitOutput();   // 入力の持ち主（InputHub）と出力エンジン（M11-2）。録音・待機・出力が同じ入力を共有する
        SetRecordButton(false);
        SetPauseButton(false);
        Title = settings.ReadOnly ? $"{AppInfo.Name}（2つ目の起動・設定は保存されません）" : AppInfo.Name;

        _loading = true;
        FolderBox.Text = settings.Folder;
        SplitBox.Text = SplitTime.Format(settings.SplitSeconds);
        CheckHolder.Child = null;
        Devices.AddFooterTrailing(InputCheckBox);   // 入力デバイス欄の下の行の右端へ
        InputCheckBox.IsChecked = settings.InputCheck;
        LoadDevices();
        Devices.Resolve = RealInputKey;
        Devices.AttachMaster(OutMeter.Gain, OutMeter.GainText);   // 全体ゲインは出力メーターと一体（Init の前に）
        Devices.Init(settings, _devices, DefaultCaptureKey());
        SyncOutputInputs();
        Devices.Changed += () => { Devices.ClearError(); CheckDuplicateRows(); CheckAsioRows(); UpdateInfo(); SyncOutputInputs(); };
        Devices.NamesChanged += () => _outWindow?.RefreshInputs();
        CheckDuplicateRows();
        CheckAsioRows();
        Devices.RefreshRequested += () => Refresh_Click(this, new RoutedEventArgs());
        Devices.GainChanged += (index, db) => { _session?.SetGain(index, db); OnInputGain(index, db); };
        Devices.MasterGainChanged += db =>
        {
            _session?.SetMasterGain(db);
            _checkMon?.SetMasterGain(db);
            _monitor?.SetMasterGain(db);
        };
        Quality.DeviceFormat = DeviceFormat;
        Quality.Init(settings);
        Quality.Changed += UpdateInfo;
        Detail.Tags = TagsTab;
        Detail.Init(settings, () => Devices.NameLabel);
        Detail.Changed += UpdateInfo;
        Presets.Init(settings);
        Presets.Prepare = CommitInputs;
        Presets.Acting += ClearErrors;
        Presets.Error += ShowError;
        Presets.Applied += ReloadFromSettings;
        Presets.Stored += UpdateInfo;   // 保存・削除では、画面も出力も作り直さない（出力中でも止まらない）
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

        Options.Init(settings);
        Plans.Init(settings);
        Options.Changed += OnOptionsChanged;
        Plans.Changed += ArmSchedule;
        InitTray();
        ArmSchedule();

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
        DisposeTray();
        StopCheckMonitor();
        if (_monitor is { } monitor)   // 待機中に終了した
        {
            _monitor = null;
            try { Task.Run(() => { monitor.Stop(); monitor.Dispose(); }).Wait(2000); } catch (Exception) { }
        }
        ShutdownOutput();
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
        FlushOutputs();   // 出力の設定も、プリセットに入る前に保存しておく
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
        CheckDuplicateRows();   // 読み込み後の設定でも、同じデバイスの重複などは赤文字で出し続ける（再読込で消えない）
        CheckAsioRows();
        SyncOutputInputs();
        ReloadOutputsFromSettings();   // プリセットの出力設定を反映（動いている出力は止める）
        Presets.Reload(null);
        UpdateInfo();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(_settings, ApplyHotkeys, OnHotkeyCapture, OnTrayOptionsChanged, PollDevices) { Owner = this }.ShowDialog();

    /// <summary>選んだデバイスから決まる、録音の元のサンプリング周波数とチャンネル数（形式・音質の一行まとめの見積もり用。未選択は 0）。</summary>
    private (int Rate, int Channels) DeviceFormat()
    {
        var infos = new List<AudioDeviceInfo>();
        foreach (var input in Devices.Inputs)
        {
            string real;
            try { real = _provider.Resolve(input.Key) ?? input.Key; }
            catch (Exception) { real = input.Key; }
            var info = _devices.FirstOrDefault(d => d.Key == real) ?? _devices.FirstOrDefault(d => d.Key == input.Key);
            if (info != null) infos.Add(info);
        }
        if (infos.Count == 0) return (0, 0);
        var rate = infos.Where(d => d.SampleRate > 0).Select(d => d.SampleRate).DefaultIfEmpty(0).Max();
        var ch = infos.Where(d => d.Channels > 0).Select(d => d.Channels).DefaultIfEmpty(0).Max();
        // 複数のミックスは最大ステレオまで（MixedSource と同じ）
        return (rate, Devices.Inputs.Count == 1 ? ch : Math.Min(2, ch));
    }

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
