using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// メイン画面の出力ランプ（M13）。出力カードの数だけ丸いランプ（中に番号）を並べる。
/// 左クリック＝出力のオン／オフ、右クリック＝メニュー（単独録音・録音に含める・詳細設定）。
/// 緑＝出力中、黄＝注意、赤＝エラー、青＝録音中。「録音に含める」の出力は、ランプの下から録音パネルの枠まで線を下ろす
/// （青＝メインの録音と連動、黄＝連動できない（出力が止まっている）、赤＝録音中なのに連動していない、薄い線＝含めない。線のクリックで切り替え）。
/// </summary>
public sealed class OutputLamps : Grid
{
    private const double LampSize = 20;
    private const double Cell = LampSize + 6;

    private readonly StackPanel _lamps = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _lines = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
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
        _lines.Margin = new Thickness(4, 0, 0, -11);
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
        for (int i = 0; i < n; i++) UpdateOne(i);
        _updating = false;
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
            var box = new CheckBox { Content = (i + 1).ToString(), Style = (Style)FindResource("OutLamp"), Width = Cell, Height = Cell };
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
            var hit = new Border { Width = Cell, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = bar };
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
        object? tag = on && mode != 0 ? "Rec"
            : on && !engine.Running && error.Length > 0 ? "Error"
            : on && engine.Running && host.OutputCaution(i) ? "Warn"
            : null;
        if (!Equals(box.Tag, tag)) box.Tag = tag;
        string state = mode != 0 ? Loc.Get("outputlamp.state.rec")
            : on && !engine.Running && error.Length > 0 ? error
            : on && engine.Running ? Loc.Get("outputlamp.state.on")
            : on ? Loc.Get("outputlamp.state.wait") : Loc.Get("outputlamp.state.off");
        var tip = Loc.Get("outputlamp.tip", i + 1, name, state);
        if (!Equals(box.ToolTip, tip)) box.ToolTip = tip;

        // 連動の線
        var bar = _bars[i];
        string key, linkTip;
        if (!flag) { key = ""; linkTip = Loc.Get("outputlamp.link.off"); }
        else if (_mainRecording)
        {
            if (mode == 2) { key = "LampRec"; linkTip = Loc.Get("outputlamp.link.linked"); }
            else { key = "LampErr"; linkTip = Loc.Get("outputlamp.link.notLinked"); }
        }
        else if (engine.Running) { key = "LampRec"; linkTip = Loc.Get("outputlamp.link.ready"); }
        else { key = "LampWarn"; linkTip = Loc.Get("outputlamp.link.stopped"); }
        if (!Equals(bar.Tag, key))
        {
            bar.Tag = key;
            if (key.Length == 0)
            {
                bar.SetResourceReference(Shape.FillProperty, "WindowFg");
                bar.Opacity = 0.18;
            }
            else
            {
                bar.SetResourceReference(Shape.FillProperty, key);
                bar.Opacity = 1;
            }
        }
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
