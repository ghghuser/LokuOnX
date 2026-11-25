using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Gokko.Core;
using Gokko.Win;
using Microsoft.Win32;
using Recorder.Core;

namespace Gokko.UI;

/// <summary>Gokko4DJ のメイン画面: 左にサウンドパッド（4×4・最大4ページ）、右に LokuOnX の操作、下にランチャー。</summary>
public sealed class MainWindow : Window
{
    /// <summary>パッドに割り当てるキー（4×4 の並びに合わせる）。</summary>
    private static readonly Key[] PadKeys = { Key.D1, Key.D2, Key.D3, Key.D4, Key.Q, Key.W, Key.E, Key.R, Key.A, Key.S, Key.D, Key.F, Key.Z, Key.X, Key.C, Key.V };
    private static readonly string[] PadKeyText = { "1", "2", "3", "4", "Q", "W", "E", "R", "A", "S", "D", "F", "Z", "X", "C", "V" };

    private readonly GokkoSettings _settings;
    private readonly PadPlayer _player = new();
    private readonly PadController _pads;
    private readonly ControlClient _client = new(ControlClient.DefaultPipe(AppContext.BaseDirectory));
    private readonly PadView[] _views = new PadView[PadBank.PerPage];
    private readonly Button[] _pageButtons = new Button[PadBank.Pages];
    private readonly ComboBox _outCombo = new() { MinWidth = 260, MaxWidth = 380 };
    private readonly Slider _master = new() { Minimum = PadConfig.MinDb, Maximum = PadConfig.MaxDb, TickFrequency = 0.5, IsSnapToTickEnabled = true, Width = 140, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _masterText = new() { Width = 70, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _statusText = new() { Margin = new Thickness(8, 4, 8, 4), TextWrapping = TextWrapping.Wrap };
    private readonly LokuPanel _loku;
    private readonly StackPanel _launcherBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _reconnect = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<Key, int> _heldKeys = new();
    private List<PadPlayer.Device> _devices = new();
    private int _page;
    private int _mouseId = -1;
    private bool _connecting;
    private bool _loadingUi;

    public MainWindow()
    {
        _settings = new GokkoSettings(System.IO.Path.Combine(AppContext.BaseDirectory, GokkoSettings.FileName));
        _page = _settings.Page;
        _pads = new PadController(_settings.Bank, _player.Mixer, LoadClip);
        _loku = new LokuPanel(_client);
        _loku.Error += ShowStatus;

        Title = Loc.Get("g.title");
        Width = 1040;
        Height = 640;
        MinWidth = 900;
        MinHeight = 560;
        Content = BuildLayout();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => ReleaseAllHeld();
        Closing += (_, _) => Shutdown();

        _client.StateChanged += s => Dispatcher.BeginInvoke(() => _loku.Apply(s));
        _client.ConnectionChanged += c => Dispatcher.BeginInvoke(() => { if (!c) _loku.Apply(null); });
        _tick.Tick += (_, _) => RefreshPlaying();
        _reconnect.Tick += async (_, _) => await TryConnect();
        Loaded += async (_, _) =>
        {
            StartOutput();
            _tick.Start();
            _reconnect.Start();
            await TryConnect();
        };
        ShowPage();
        RefreshLauncher();
    }

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 上の帯: 出力先・全体の音量・全停止
        var top = new DockPanel { Margin = new Thickness(8, 8, 8, 4), LastChildFill = false };
        var label = new TextBlock { Text = Loc.Get("g.out.label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _outCombo.ToolTip = Loc.Get("g.out.tip");
        _devices = PadPlayer.Devices();
        _loadingUi = true;
        _outCombo.Items.Add(Loc.Get("g.out.default"));
        foreach (var d in _devices) _outCombo.Items.Add(d.Name);
        var idx = _devices.FindIndex(d => d.Id == _settings.OutputDeviceId);
        _outCombo.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        _loadingUi = false;
        _outCombo.SelectionChanged += (_, _) => { if (!_loadingUi) OutputChanged(); };

        _master.Value = _settings.MasterDb;
        _player.Mixer.MasterDb = _settings.MasterDb;
        _master.ValueChanged += (_, _) => MasterChanged();
        ShowMaster();
        var stop = new Button { Content = Loc.Get("g.stopall"), ToolTip = Loc.Get("g.stopall.tip"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(12, 0, 0, 0) };
        stop.Click += (_, _) => _pads.StopAll();

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(label);
        left.Children.Add(_outCombo);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(new TextBlock { Text = Loc.Get("g.master.label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        right.Children.Add(_master);
        right.Children.Add(_masterText);
        right.Children.Add(stop);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(left);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // 中央: 左＝パッド（ページ切替つき）、右＝LokuOnX の操作
        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        var padArea = new Grid();
        padArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        padArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var pages = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 2) };
        for (var p = 0; p < PadBank.Pages; p++)
        {
            var page = p;
            var b = new Button { Content = Loc.Get("g.page", p + 1), Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 4, 0) };
            b.Click += (_, _) => { _page = page; _settings.Page = page; ShowPage(); SaveSettings(); };
            _pageButtons[p] = b;
            pages.Children.Add(b);
        }
        padArea.Children.Add(pages);
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 4, Columns = 4 };
        for (var i = 0; i < PadBank.PerPage; i++)
        {
            var v = new PadView(i, PadKeyText[i]);
            _views[i] = v;
            WirePad(v);
            grid.Children.Add(v);
        }
        Grid.SetRow(grid, 1);
        padArea.Children.Add(grid);
        middle.Children.Add(padArea);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _loku };
        Grid.SetColumn(scroll, 1);
        middle.Children.Add(scroll);
        Grid.SetRow(middle, 1);
        root.Children.Add(middle);

        Grid.SetRow(_launcherBar, 2);
        root.Children.Add(_launcherBar);
        Grid.SetRow(_statusText, 3);
        root.Children.Add(_statusText);
        return root;
    }

    // ---- パッド ----

    private int IdOf(PadView v) => PadBank.Id(_page, v.Slot);

    private void WirePad(PadView v)
    {
        v.MouseLeftButtonDown += (_, e) =>
        {
            var id = IdOf(v);
            if (_settings.Bank[id].IsEmpty) { EditPad(id); e.Handled = true; return; }
            v.CaptureMouse();
            _mouseId = id;
            Press(id);
            e.Handled = true;
        };
        v.MouseLeftButtonUp += (_, e) =>
        {
            v.ReleaseMouseCapture();
            if (_mouseId >= 0) _pads.Release(_mouseId);
            _mouseId = -1;
            e.Handled = true;
        };
        var menu = new ContextMenu();
        var set = new MenuItem { Header = Loc.Get("g.pad.set") };
        set.Click += (_, _) => EditPad(IdOf(v));
        var clear = new MenuItem { Header = Loc.Get("g.pad.clear") };
        clear.Click += (_, _) => { var id = IdOf(v); _pads.Release(id); _settings.Bank[id] = PadConfig.Empty; SaveSettings(); ShowPage(); };
        menu.Items.Add(set);
        menu.Items.Add(clear);
        v.ContextMenu = menu;
        v.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var id = IdOf(v);
                var old = _settings.Bank[id];
                _settings.Bank[id] = new PadConfig(old.Name, files[0], old.Mode, old.VolumeDb, old.Color);
                SaveSettings();
                ShowPage();
                ShowStatus(Loc.Get("g.pad.dropped", System.IO.Path.GetFileName(files[0]), v.KeyText));
            }
        };
    }

    private void Press(int id)
    {
        if (!_pads.Press(id))
        {
            var p = _settings.Bank[id];
            if (!p.IsEmpty)
            {
                AudioLoader.Load(p.FilePath, out var err);
                ShowStatus(Loc.Get("g.pad.err.load", p.Title, err));
            }
        }
    }

    private PadClip? LoadClip(string path) => AudioLoader.Load(path, out _);

    private void EditPad(int id)
    {
        var dlg = new PadDialog(_settings.Bank[id]) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        _pads.Release(id);
        _settings.Bank[id] = dlg.Result;
        SaveSettings();
        ShowPage();
    }

    private void ShowPage()
    {
        for (var i = 0; i < PadBank.PerPage; i++) _views[i].Show(_settings.Bank[PadBank.Id(_page, i)]);
        for (var p = 0; p < PadBank.Pages; p++)
            _pageButtons[p].FontWeight = p == _page ? FontWeights.Bold : FontWeights.Normal;
    }

    private void RefreshPlaying()
    {
        for (var i = 0; i < PadBank.PerPage; i++)
        {
            var id = PadBank.Id(_page, i);
            var playing = _player.Mixer.IsPlaying(id);
            _views[i].SetPlaying(playing, playing ? _player.Mixer.Progress(id) : 0);
        }
    }

    // ---- キーボード ----

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _pads.StopAll(); e.Handled = true; return; }
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None) return;
        var slot = Array.IndexOf(PadKeys, e.Key);
        if (slot < 0 || _heldKeys.ContainsKey(e.Key)) return;
        var id = PadBank.Id(_page, slot);
        if (_settings.Bank[id].IsEmpty) return;
        _heldKeys[e.Key] = id;
        Press(id);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_heldKeys.TryGetValue(e.Key, out var id)) return;
        _heldKeys.Remove(e.Key);
        _pads.Release(id);
        e.Handled = true;
    }

    private void ReleaseAllHeld()
    {
        foreach (var id in _heldKeys.Values) _pads.Release(id);
        _heldKeys.Clear();
        if (_mouseId >= 0) { _pads.Release(_mouseId); _mouseId = -1; }
    }

    // ---- 出力 ----

    private void StartOutput()
    {
        var err = _player.Start(_settings.OutputDeviceId);
        if (err != null) ShowStatus(Loc.Get("g.out.err", err));
    }

    private void OutputChanged()
    {
        var i = _outCombo.SelectedIndex;
        _settings.OutputDeviceId = i > 0 && i - 1 < _devices.Count ? _devices[i - 1].Id : "";
        SaveSettings();
        StartOutput();
    }

    private void MasterChanged()
    {
        _settings.MasterDb = _master.Value;
        _player.Mixer.MasterDb = _master.Value;
        ShowMaster();
    }

    private void ShowMaster() => _masterText.Text = _master.Value <= PadConfig.MinDb ? Loc.Get("g.master.mute") : $"{_master.Value:0.0} dB";

    // ---- LokuOnX との接続 ----

    private async Task TryConnect()
    {
        if (_connecting || _client.Connected) return;
        _connecting = true;
        try { await _client.ConnectAsync(); }
        catch (Exception) { /* つながらなければ次の周期でやり直す */ }
        finally { _connecting = false; }
    }

    // ---- ランチャー ----

    private void RefreshLauncher()
    {
        _launcherBar.Children.Clear();
        var lokuPath = System.IO.Path.Combine(AppContext.BaseDirectory, AppInfo.Name + ".exe");
        var exists = File.Exists(lokuPath);
        var loku = new Button { Content = Loc.Get("g.launcher.loku"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = exists, ToolTip = Loc.Get(exists ? "g.launcher.loku.tip" : "g.launcher.loku.missing") };
        loku.Click += (_, _) => Launch(lokuPath);
        _launcherBar.Children.Add(loku);
        foreach (var path in _settings.Launcher.ToList())
        {
            var p = path;
            var b = new Button { Content = LauncherList.Title(p), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), ToolTip = Loc.Get("g.launcher.app.tip", LauncherList.Title(p), p) };
            b.Click += (_, _) => Launch(p);
            var menu = new ContextMenu();
            var rm = new MenuItem { Header = Loc.Get("g.launcher.menu.remove") };
            rm.Click += (_, _) => { _settings.Launcher.Remove(p); SaveSettings(); RefreshLauncher(); };
            menu.Items.Add(rm);
            b.ContextMenu = menu;
            _launcherBar.Children.Add(b);
        }
        if (_settings.Launcher.Count < LauncherList.Max)
        {
            var add = new Button { Content = "＋", Padding = new Thickness(12, 4, 12, 4), ToolTip = Loc.Get("g.launcher.add.tip") };
            add.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Title = Loc.Get("g.launcher.pick.title"), Filter = Loc.Get("g.launcher.pick.filter") };
                if (dlg.ShowDialog(this) != true) return;
                _settings.Launcher = LauncherList.Clean(_settings.Launcher.Append(dlg.FileName));
                SaveSettings();
                RefreshLauncher();
            };
            _launcherBar.Children.Add(add);
        }
    }

    private void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? "" });
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Get("g.launcher.err.start", LauncherList.Title(path), ex.Message));
        }
    }

    // ---- 共通 ----

    private void ShowStatus(string text) => Dispatcher.BeginInvoke(() => _statusText.Text = text);

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private void Shutdown()
    {
        _tick.Stop();
        _reconnect.Stop();
        _pads.StopAll();
        _client.Dispose();
        _player.Dispose();
        SaveSettings();
    }
}
