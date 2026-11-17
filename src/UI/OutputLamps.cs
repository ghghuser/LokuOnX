using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// メイン画面の出力ランプ（M13）。出力カードの数だけ丸いランプ（中に番号）を並べる。
/// 左クリック＝出力のオン／オフ、右クリック＝メニュー（単独録音・録音に含める・詳細設定）。
/// 緑＝出力中、黄＝注意、赤＝エラー、青＝録音中。「録音に含める」の出力は、ランプの下から録音パネルの枠まで線を下ろす
/// （青＝メインの録音と連動、白っぽい（ライト系は黒っぽい）＝出力が止まっていて録れない、赤＝録音中なのに連動していない、薄い線＝含めない。線のクリックで切り替え）。
/// </summary>
public sealed class OutputLamps : Grid
{
    private const double LampSize = 20;
    private const double Cell = LampSize + 4;
    private const double Gap = 3;   // ランプどうしの間隔

    private readonly StackPanel _lamps = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _lines = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly Rectangle _link = new() { Width = 8, Height = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<CheckBox> _boxes = new();
    private readonly List<Rectangle> _bars = new();
    private IOutputHost? _host;
    private bool _mainRecording;
    private bool _updating;

    /// <summary>右クリックの「詳細設定」で出力ウィンドウを開く（index 番目のカードへ）。</summary>
    public Action<int>? Open { get; set; }

    public OutputLamps()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(3, 2, 3, 2),
            Child = _lamps,
            VerticalAlignment = VerticalAlignment.Center
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        SetRow(frame, 1);
        Children.Add(frame);
        // 線は、ランプの枠の下から、このコントロールの下端（上段の余白を越えて録音パネルの枠）まで
        // 上は、ランプの円の下端（枠の線・余白ぶん）まで食い込ませて、円と線の隙間をなくす
        _lines.Margin = new Thickness(4, -5, 0, -11);
        // ランプの枠とI/Oルーティングボタンをつなぐ線（全出力のまとめの色）
        SetRow(_link, 1);
        SetColumn(_link, 1);
        Children.Add(_link);
        SetRow(_lines, 2);
        Children.Add(_lines);
        Visibility = Visibility.Collapsed;
    }

    /// <summary>状態を読み直して表示する（0.5秒ごと・録音の開始停止のとき）。</summary>
    public void Refresh(IOutputHost host, bool mainRecording)
    {
        _host = host;
        _mainRecording = mainRecording;
        int n = host.Hub.Engines.Count < host.Configs.Count ? host.Hub.Engines.Count : host.Configs.Count;
        if (n != _boxes.Count) Rebuild(n);
        Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        _updating = true;
        _counts = new int[4];
        for (int i = 0; i < n; i++) UpdateOne(i);
        _updating = false;
        // まとめの線: 青＞緑＞黄＞赤。どれも無ければ薄い線
        string key = _counts[0] > 0 ? "LampRec" : _counts[1] > 0 ? "LampOn" : _counts[2] > 0 ? "LampWarn" : _counts[3] > 0 ? "LampErr" : "";
        Paint(_link, key);
    }

    private int[] _counts = new int[4];   // 青・緑・黄・赤のランプの数

    private static void Paint(Rectangle r, string key)
    {
        if (Equals(r.Tag, key)) return;
        r.Tag = key;
        if (key.Length == 0)
        {
            r.SetResourceReference(Shape.FillProperty, "LinkOff");
            r.Opacity = 1;
        }
        else
        {
            r.SetResourceReference(Shape.FillProperty, key);
            r.Opacity = 1;
        }
    }

    private void Rebuild(int n)
    {
        _lamps.Children.Clear();
        _lines.Children.Clear();
        _boxes.Clear();
        _bars.Clear();
        for (int i = 0; i < n; i++)
        {
            int index = i;
            var box = new CheckBox { Content = OutputConfig.Letter(i), Style = (Style)FindResource("OutLamp"), Width = Cell, Height = Cell, Margin = new Thickness(0, 0, i < n - 1 ? Gap : 0, 0) };
            box.Click += (_, _) =>
            {
                if (_updating || _host == null) return;
                _host.Hub.Engines[index].On = box.IsChecked == true;
                Refresh(_host, _mainRecording);
            };
            box.ContextMenuOpening += (_, e) => { e.Handled = true; ShowMenu(index, box); };
            box.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(index, box); };
            _boxes.Add(box);
            _lamps.Children.Add(box);

            var bar = new Rectangle { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            var hit = new Border { Width = Cell, Margin = new Thickness(0, 0, i < n - 1 ? Gap : 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = bar };
            hit.MouseLeftButtonUp += (_, _) =>
            {
                if (_host == null) return;
                _host.SetOutputRec(index, !_host.OutputRec(index));
                Refresh(_host, _mainRecording);
            };
            _bars.Add(bar);
            _lines.Children.Add(hit);
        }
    }

    private void UpdateOne(int i)
    {
        var host = _host!;
        var engine = host.Hub.Engines[i];
        var name = host.Configs[i].Name;
        int mode = host.OutputRecMode(i);
        bool flag = host.OutputRec(i);
        bool on = engine.On;
        var error = engine.Error;

        var box = _boxes[i];
        if (box.IsChecked != on) box.IsChecked = on;
        var deviceId = host.Configs[i].DeviceId;
        object? tag = on && mode != 0 ? "Rec"
            : !engine.Running && ((on && error.Length > 0) || (!on && (deviceId.Length == 0 || host.IsLoopOutput(deviceId)))) ? "Error"   // オフでも、オンにするとエラーになるものは、マウスオーバーの輪が赤
            : host.OutputCaution(i) ? "Warn"   // 出力先を別カードと共有（オフのときも、オンにしたときの黄を輪で見せる）
            : null;
        if (on)
        {
            if (Equals(tag, "Rec")) _counts[0]++;
            else if (Equals(tag, "Error")) _counts[3]++;
            else if (Equals(tag, "Warn")) _counts[2]++;
            else if (engine.Running) _counts[1]++;
        }
        if (!Equals(box.Tag, tag)) box.Tag = tag;
        string state = mode != 0 ? Loc.Get("outputlamp.state.rec")
            : on && !engine.Running && error.Length > 0 ? error
            : on && engine.Running ? Loc.Get("outputlamp.state.on")
            : on ? Loc.Get("outputlamp.state.wait") : Loc.Get("outputlamp.state.off");
        var tip = Loc.Get("outputlamp.tip", i + 1, name, state);
        if (!Equals(box.ToolTip, tip)) box.ToolTip = tip;

        // 連動の線: 「録音に含める」がオンのときはランプの色と同じ（青＝録音中、緑＝出力中、黄＝注意、赤＝エラー）。
        // 例外: メインの録音中なのに連動していなければ赤（ランプが青でも、メインとは別の単独録音のとき）、出力が止まっていて録れないときは白っぽい色（ライト系は黒っぽい色）
        var bar = _bars[i];
        string key, linkTip;
        if (!flag) { key = ""; linkTip = Loc.Get("outputlamp.link.off"); }
        else if (_mainRecording && mode != 2) { key = "LampErr"; linkTip = Loc.Get("outputlamp.link.notLinked"); }
        else if (mode != 0) { key = "LampRec"; linkTip = Loc.Get("outputlamp.link.linked"); }
        else if (on && !engine.Running && error.Length > 0) { key = "LampErr"; linkTip = Loc.Get("outputlamp.link.error"); }
        else if (!engine.Running) { key = "LinkStop"; linkTip = Loc.Get("outputlamp.link.stopped"); }
        else if (Equals(tag, "Warn")) { key = "LampWarn"; linkTip = Loc.Get("outputlamp.link.ready"); }
        else { key = "LampOn"; linkTip = Loc.Get("outputlamp.link.ready"); }
        Paint(bar, key);
        if (bar.Parent is FrameworkElement hit && !Equals(hit.ToolTip, linkTip)) hit.ToolTip = linkTip;
    }

    private void ShowMenu(int i, CheckBox box)
    {
        var host = _host;
        if (host == null || i >= host.Hub.Engines.Count) return;
        var engine = host.Hub.Engines[i];
        int mode = host.OutputRecMode(i);
        var menu = new ContextMenu { PlacementTarget = box, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        var on = new MenuItem { Header = Loc.Get("outputlamp.menu.on"), IsCheckable = true, IsChecked = engine.On };
        on.Click += (_, _) => { engine.On = on.IsChecked; Refresh(host, _mainRecording); };
        var solo = new MenuItem
        {
            Header = Loc.Get("outputlamp.menu.solo"),
            IsCheckable = true,
            IsChecked = mode == 1,
            IsEnabled = mode == 1 || (mode == 0 && engine.Running)
        };
        solo.Click += (_, _) => { host.ToggleOutputRecording(i); Refresh(host, _mainRecording); };
        if (mode == 2) solo.ToolTip = Loc.Get("outputlamp.menu.soloLinked");
        var with = new MenuItem { Header = Loc.Get("outputlamp.menu.with"), IsCheckable = true, IsChecked = host.OutputRec(i) };
        with.Click += (_, _) => { host.SetOutputRec(i, with.IsChecked); Refresh(host, _mainRecording); };
        var detail = new MenuItem { Header = Loc.Get("outputlamp.menu.detail") };
        detail.Click += (_, _) => Open?.Invoke(i);
        menu.Items.Add(on);
        menu.Items.Add(solo);
        menu.Items.Add(with);
        menu.Items.Add(new Separator());
        menu.Items.Add(detail);
        menu.IsOpen = true;
    }
}
