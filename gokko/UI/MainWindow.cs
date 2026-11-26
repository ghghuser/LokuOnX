using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Gokko.Core;
using Gokko.Win;
using Microsoft.Win32;
using Recorder.Core;
using Recorder.UI;

namespace Gokko.UI;

/// <summary>Gokko4DJ のメイン画面: 左にサウンドパッドのタイル（横・縦とも最大10）、右にタブ（LokuOnX の操作）、下にランチャー。</summary>
public sealed class MainWindow : Window
{
    /// <summary>テンキーに合わせた、左上4×4のキー（行ごと）。</summary>
    private static readonly Key[] PadKeys =
    {
        Key.NumPad7, Key.NumPad8, Key.NumPad9, Key.Divide,
        Key.NumPad4, Key.NumPad5, Key.NumPad6, Key.Multiply,
        Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.Subtract,
        Key.NumPad0, Key.Decimal, Key.Enter, Key.Add,
    };
    private static readonly string[] PadKeyText = { "7", "8", "9", "/", "4", "5", "6", "*", "1", "2", "3", "-", "0", ".", "↵", "+" };

    private readonly GokkoSettings _settings;
    private readonly PadPlayer _player = new();
    private readonly PadController _pads;
    private readonly ControlClient _client = new(ControlClient.DefaultPipe(AppContext.BaseDirectory));
    private readonly Dictionary<int, PadView> _views = new();
    private readonly Dictionary<Key, int> _keyMap = new();
    private readonly Dictionary<string, Border> _segments = new();
    private readonly List<Border> _swatches = new();
    private readonly Grid _tiles = new() { Margin = new Thickness(0) };
    private readonly ComboBox _outCombo = new() { MinWidth = 240, MaxWidth = 360 };
    private readonly Slider _master = new() { Minimum = PadConfig.MinDb, Maximum = PadConfig.MaxDb, TickFrequency = 0.5, IsSnapToTickEnabled = true, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _masterText = new() { Width = 62, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _colsText = new() { Width = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _rowsText = new() { Width = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusText = new() { Margin = new Thickness(12, 2, 12, 8), TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12 };
    private readonly LokuPanel _loku;
    private readonly StackPanel _launcherBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4, 12, 4) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _reconnect = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<Key, int> _heldKeys = new();
    private readonly MessageWindow _msg;
    private readonly GokkoTray _tray;
    private List<PadPlayer.Device> _devices = new();
    private int _selColor;   // 枠に付ける色（0〜7）。-1＝消しゴム
    private Point _downPoint;
    private int _downId = -1;
    private bool _dragged;
    private bool _connecting;
    private bool _loadingUi;
    private bool _closing;

    public MainWindow()
    {
        _settings = new GokkoSettings(System.IO.Path.Combine(AppContext.BaseDirectory, GokkoSettings.FileName));
        if (!_settings.Ini.Existed) SeedSamples();
        _pads = new PadController(_settings.Bank, _player.Mixer, LoadClip);
        _loku = new LokuPanel(_client);
        _loku.Error += ShowStatus;

        Title = Loc.Get("g.title");
        Width = 1120;
        Height = 700;
        MinWidth = 940;
        MinHeight = 560;
        ThemeManager.Attach(this);
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Gokko4DJ.ico")); }
        catch (Exception) { /* アイコンが無くても動く */ }
        Content = BuildLayout();
        BuildTiles();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => ReleaseAllHeld();
        Closing += OnClosing;

        _msg = new MessageWindow();
        _tray = new GokkoTray(_msg) { MenuProvider = BuildTrayMenu };
        _tray.OpenRequested += ShowFromTray;
        _tray.CommandSelected += OnTrayCommand;
        _tray.SetTip(Loc.Get("g.tray.tip"));
        _tray.Visible = true;

        _client.StateChanged += s => Dispatcher.BeginInvoke(() => _loku.Apply(s));
        _client.ConnectionChanged += c => Dispatcher.BeginInvoke(() => { if (!c) _loku.Apply(null); });
        _tick.Tick += (_, _) => RefreshPlaying();
        _reconnect.Tick += async (_, _) => await TryConnect();
        Loaded += async (_, _) =>
        {
            FadeIn();
            StartOutput();
            _tick.Start();
            _reconnect.Start();
            await TryConnect();
        };
        RefreshLauncher();
    }

    /// <summary>初回起動（設定ファイルが無い）のとき、同梱のサンプルSEを先頭のパッドへ入れる。</summary>
    private void SeedSamples()
    {
        var i = 0;
        foreach (var (file, name) in SeFolder.Samples)
        {
            var path = System.IO.Path.Combine(SeFolder.Path, file);
            if (!File.Exists(path)) continue;
            _settings.Bank[i] = new PadConfig(name, path, PadMode.Once, 0, i % PadConfig.ColorCount);
            i++;
        }
    }

    // ---- レイアウト ----

    private UIElement BuildLayout()
    {
        var root = new Grid();
        for (var i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // 1段目: 出力先・全体の音量・全停止・設定
        var top = new DockPanel { Margin = new Thickness(12, 10, 12, 4), LastChildFill = false };
        var settings = new Button { Content = "⚙", ToolTip = Loc.Get("g.settings.tip"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        settings.Click += (_, _) => new AboutWindow(_settings.Theme, ChangeTheme) { Owner = this }.ShowDialog();
        DockPanel.SetDock(settings, Dock.Right);
        top.Children.Add(settings);
        var stop = new Button { Content = Loc.Get("g.stopall"), ToolTip = Loc.Get("g.stopall.tip"), Margin = new Thickness(12, 0, 0, 0) };
        stop.Click += (_, _) => _pads.StopAll();
        DockPanel.SetDock(stop, Dock.Right);
        top.Children.Add(stop);
        var masterBox = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        masterBox.Children.Add(new TextBlock { Text = Loc.Get("g.master.label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        masterBox.Children.Add(_master);
        masterBox.Children.Add(_masterText);
        DockPanel.SetDock(masterBox, Dock.Right);
        top.Children.Add(masterBox);
        var outBox = new StackPanel { Orientation = Orientation.Horizontal };
        outBox.Children.Add(new TextBlock { Text = Loc.Get("g.out.label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _outCombo.ToolTip = Loc.Get("g.out.tip");
        _devices = PadPlayer.Devices();
        _loadingUi = true;
        _outCombo.Items.Add(Loc.Get("g.out.default"));
        foreach (var d in _devices) _outCombo.Items.Add(d.Name);
        var idx = _devices.FindIndex(d => d.Id == _settings.OutputDeviceId);
        _outCombo.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        _loadingUi = false;
        _outCombo.SelectionChanged += (_, _) => { if (!_loadingUi) OutputChanged(); };
        outBox.Children.Add(_outCombo);
        top.Children.Add(outBox);
        _master.Value = _settings.MasterDb;
        _player.Mixer.MasterDb = _settings.MasterDb;
        _master.ValueChanged += (_, _) => MasterChanged();
        ShowMaster();
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // 2段目: タイルの数（横・縦）と、枠の色
        var bar = new WrapPanel { Margin = new Thickness(12, 2, 12, 8) };
        bar.Children.Add(Stepper("g.size.cols", _colsText, () => ChangeSize(1, 0), () => ChangeSize(-1, 0)));
        bar.Children.Add(Stepper("g.size.rows", _rowsText, () => ChangeSize(0, 1), () => ChangeSize(0, -1)));
        var pal = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        pal.Children.Add(new TextBlock { Text = Loc.Get("g.border.label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = Loc.Get("g.border.tip") });
        for (var c = -1; c < PadConfig.ColorCount; c++)
        {
            var color = c;
            var sw = new Border { Width = 20, Height = 20, Margin = new Thickness(0, 0, 5, 0), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, ToolTip = c < 0 ? Loc.Get("g.border.erase") : null };
            if (c < 0)
            {
                sw.SetResourceReference(BackgroundProperty, "CtrlBg");
                sw.Child = new TextBlock { Text = "✕", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
            }
            else sw.Background = PadBrushes.Fill(c);
            sw.MouseLeftButtonUp += (_, _) => { _selColor = color; ShowPalette(); };
            _swatches.Add(sw);
            pal.Children.Add(sw);
        }
        bar.Children.Add(pal);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);

        // 中央: タイル＋右のタブ
        var middle = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(372) });
        Grid.SetColumn(_tiles, 0);
        middle.Children.Add(_tiles);
        var tabs = new TabControl { Margin = new Thickness(14, 0, 0, 0) };
        tabs.Items.Add(new TabItem { Header = Loc.Get("g.tab.loku"), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _loku } });
        Grid.SetColumn(tabs, 1);
        middle.Children.Add(tabs);
        Grid.SetRow(middle, 2);
        root.Children.Add(middle);

        Grid.SetRow(_launcherBar, 3);
        root.Children.Add(_launcherBar);
        Grid.SetRow(_statusText, 4);
        root.Children.Add(_statusText);
        ShowPalette();
        return root;
    }

    private static UIElement Stepper(string label, TextBlock value, Action up, Action down)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        p.Children.Add(new TextBlock { Text = Loc.Get(label), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var u = new Button { Content = "▲", Padding = new Thickness(7, 2, 7, 2), ToolTip = Loc.Get("g.size.up") };
        var d = new Button { Content = "▼", Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = Loc.Get("g.size.down") };
        u.Click += (_, _) => up();
        d.Click += (_, _) => down();
        p.Children.Add(value);
        p.Children.Add(u);
        p.Children.Add(d);
        return p;
    }

    private void ShowPalette()
    {
        for (var i = 0; i < _swatches.Count; i++)
        {
            var selected = i - 1 == _selColor;
            _swatches[i].BorderBrush = selected ? (Brush)FindResource("Fg") : Brushes.Transparent;
        }
    }

    private void ChangeTheme(string mode)
    {
        _settings.Theme = mode;
        ThemeManager.Apply(mode);
        ShowPalette();
        SaveSettings();
    }

    // ---- タイル ----

    private void ChangeSize(int dCols, int dRows)
    {
        var c = Math.Clamp(_settings.Cols + dCols, 1, PadBank.Side);
        var r = Math.Clamp(_settings.Rows + dRows, 1, PadBank.Side);
        if (c == _settings.Cols && r == _settings.Rows) return;
        _settings.Cols = c;
        _settings.Rows = r;
        SaveSettings();
        BuildTiles();
    }

    private void BuildTiles()
    {
        ReleaseAllHeld();
        _tiles.Children.Clear();
        _tiles.RowDefinitions.Clear();
        _tiles.ColumnDefinitions.Clear();
        _views.Clear();
        _keyMap.Clear();
        _segments.Clear();
        var cols = _settings.Cols;
        var rows = _settings.Rows;
        _colsText.Text = cols.ToString();
        _rowsText.Text = rows.ToString();
        for (var c = 0; c < cols; c++)
        {
            _tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 56 });
            if (c < cols - 1) _tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        }
        for (var r = 0; r < rows; r++)
        {
            _tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 44 });
            if (r < rows - 1) _tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        }
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var id = PadBank.Id(r, c);
                var v = new PadView(id);
                Grid.SetRow(v, r * 2);
                Grid.SetColumn(v, c * 2);
                WirePad(v);
                _tiles.Children.Add(v);
                _views[id] = v;
                if (r < 4 && c < 4) _keyMap[PadKeys[r * 4 + c]] = id;
                if (c < cols - 1) AddSegment(true, r, c, r * 2, c * 2 + 1);
                if (r < rows - 1) AddSegment(false, r, c, r * 2 + 1, c * 2);
            }
        }
        RefreshTiles();
    }

    private void AddSegment(bool vertical, int r, int c, int gridRow, int gridCol)
    {
        var key = GokkoSettings.BorderKey(vertical, r, c);
        var inner = new Border { CornerRadius = new CornerRadius(2), IsHitTestVisible = false, Margin = vertical ? new Thickness(3, 0, 3, 0) : new Thickness(0, 3, 0, 3) };
        var seg = new Border { Background = Brushes.Transparent, Child = inner, ToolTip = Loc.Get("g.border.tip") };
        Grid.SetRow(seg, gridRow);
        Grid.SetColumn(seg, gridCol);
        seg.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            ToggleSegment(key);
            e.Handled = true;
        };
        _tiles.Children.Add(seg);
        _segments[key] = seg;
        ShowSegment(key);
    }

    private void ShowSegment(string key)
    {
        if (!_segments.TryGetValue(key, out var seg) || seg.Child is not Border inner) return;
        inner.Background = _settings.Borders.TryGetValue(key, out var ci) ? PadBrushes.Fill(ci) : null;
    }

    private void ToggleSegment(string key)
    {
        if (_selColor < 0 || (_settings.Borders.TryGetValue(key, out var cur) && cur == _selColor)) _settings.Borders.Remove(key);
        else _settings.Borders[key] = _selColor;
        ShowSegment(key);
        SaveSettings();
    }

    private string KeyText(int id)
    {
        var r = id / PadBank.Side;
        var c = id % PadBank.Side;
        return r < 4 && c < 4 ? PadKeyText[r * 4 + c] : "";
    }

    private void RefreshTiles()
    {
        foreach (var (id, v) in _views) v.Show(_settings.Bank[id], KeyText(id));
    }

    private void WirePad(PadView v)
    {
        v.LoopClicked += pv =>
        {
            var p = _settings.Bank[pv.Id];
            if (p.IsEmpty) return;
            _pads.Forget(pv.Id);
            _settings.Bank[pv.Id] = new PadConfig(p.Name, p.FilePath, p.Mode == PadMode.Loop ? PadMode.Once : PadMode.Loop, p.VolumeDb, p.Color);
            SaveSettings();
            RefreshTiles();
        };
        v.MouseLeftButtonDown += (_, e) =>
        {
            _downPoint = e.GetPosition(this);
            _downId = v.Id;
            _dragged = false;
            v.CaptureMouse();
            var p = _settings.Bank[v.Id];
            if (!p.IsEmpty && p.Mode == PadMode.Hold) Press(v.Id);   // 押している間だけ＝押した瞬間に鳴らす
            e.Handled = true;
        };
        v.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _downId != v.Id || _dragged) return;
            var d = e.GetPosition(this) - _downPoint;
            if (Math.Abs(d.X) < 8 && Math.Abs(d.Y) < 8) return;
            if (_settings.Bank[v.Id].IsEmpty) return;
            _dragged = true;
            v.ReleaseMouseCapture();
            _pads.Release(v.Id);
            DragDrop.DoDragDrop(v, new DataObject("GokkoPad", v.Id), DragDropEffects.Move);
            _downId = -1;
            _dragged = false;
        };
        v.MouseLeftButtonUp += (_, e) =>
        {
            v.ReleaseMouseCapture();
            e.Handled = true;
            if (_downId != v.Id) return;
            _downId = -1;
            if (_dragged) { _dragged = false; return; }
            var p = _settings.Bank[v.Id];
            if (p.IsEmpty) EditPad(v.Id);
            else if (p.Mode == PadMode.Hold) _pads.Release(v.Id);
            else Press(v.Id);
        };
        var menu = new ContextMenu();
        var set = new MenuItem { Header = Loc.Get("g.pad.set") };
        set.Click += (_, _) => EditPad(v.Id);
        var clear = new MenuItem { Header = Loc.Get("g.pad.clear") };
        clear.Click += (_, _) => { _pads.Forget(v.Id); _settings.Bank[v.Id] = PadConfig.Empty; SaveSettings(); RefreshTiles(); };
        menu.Items.Add(set);
        menu.Items.Add(clear);
        v.ContextMenu = menu;
        v.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("GokkoPad") || e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        v.Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetDataPresent("GokkoPad") && e.Data.GetData("GokkoPad") is int from)
            {
                if (from == v.Id) return;
                _pads.Forget(from);
                _pads.Forget(v.Id);
                _settings.Bank.Swap(from, v.Id);
                SaveSettings();
                RefreshTiles();
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var old = _settings.Bank[v.Id];
                _pads.Forget(v.Id);
                _settings.Bank[v.Id] = new PadConfig(old.Name, files[0], old.Mode, old.VolumeDb, old.IsEmpty ? v.Id % PadConfig.ColorCount : old.Color);
                SaveSettings();
                RefreshTiles();
                ShowStatus(Loc.Get("g.pad.dropped", System.IO.Path.GetFileName(files[0]), KeyText(v.Id)));
            }
        };
    }

    private void Press(int id)
    {
        if (_pads.Press(id)) return;
        var p = _settings.Bank[id];
        if (p.IsEmpty) return;
        AudioLoader.Load(p.FilePath, out var err);
        ShowStatus(Loc.Get("g.pad.err.load", p.Title, err));
    }

    private static PadClip? LoadClip(string path) => AudioLoader.Load(path, out _);

    private void EditPad(int id)
    {
        var dlg = new PadDialog(_settings.Bank[id]) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        _pads.Forget(id);
        _settings.Bank[id] = dlg.Result;
        SaveSettings();
        RefreshTiles();
    }

    private void RefreshPlaying()
    {
        foreach (var (id, v) in _views)
        {
            var playing = _player.Mixer.IsPlaying(id);
            v.SetPlaying(playing, playing ? _player.Mixer.Progress(id) : 0);
        }
    }

    // ---- キーボード（テンキー） ----

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _pads.StopAll(); e.Handled = true; return; }
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!_keyMap.TryGetValue(e.Key, out var id) || _heldKeys.ContainsKey(e.Key)) return;
        var p = _settings.Bank[id];
        if (p.IsEmpty) return;
        e.Handled = true;
        if (p.Mode == PadMode.Hold) { _heldKeys[e.Key] = id; }
        Press(id);
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
        if (_downId >= 0) { _pads.Release(_downId); _downId = -1; }
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
        var loku = new Button { Content = Loc.Get("g.launcher.loku"), Margin = new Thickness(0, 0, 6, 0), IsEnabled = exists, ToolTip = Loc.Get(exists ? "g.launcher.loku.tip" : "g.launcher.loku.missing") };
        loku.Click += (_, _) => Launch(lokuPath);
        _launcherBar.Children.Add(loku);
        foreach (var path in _settings.Launcher.ToList())
        {
            var p = path;
            var b = new Button { Content = LauncherList.Title(p), Margin = new Thickness(0, 0, 6, 0), ToolTip = Loc.Get("g.launcher.app.tip", LauncherList.Title(p), p) };
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
            var add = new Button { Content = "＋", ToolTip = Loc.Get("g.launcher.add.tip") };
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

    // ---- トレイ・フェード ----

    private IReadOnlyList<TrayItem> BuildTrayMenu() => new[]
    {
        new TrayItem(1, Loc.Get("g.tray.show")),
        new TrayItem(2, Loc.Get("g.tray.stop")),
        TrayItem.Separator,
        new TrayItem(3, Loc.Get("g.tray.exit")),
    };

    private void OnTrayCommand(int id)
    {
        if (id == 1) ShowFromTray();
        else if (id == 2) _pads.StopAll();
        else if (id == 3) Close();
    }

    private void ShowFromTray()
    {
        if (!IsVisible) { Show(); FadeIn(); }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void FadeIn()
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>閉じるときは、すっと薄くしてから終わる。</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            _closing = true;
            var a = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160));
            a.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, a);
            return;
        }
        _tick.Stop();
        _reconnect.Stop();
        _pads.StopAll();
        _tray.Dispose();
        _msg.Dispose();
        _client.Dispose();
        _player.Dispose();
        SaveSettings();
    }

    // ---- 共通 ----

    private void ShowStatus(string text) => Dispatcher.BeginInvoke(() => _statusText.Text = text);

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
}
