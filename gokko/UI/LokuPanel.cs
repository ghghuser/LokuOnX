using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Gokko.Core;
using Recorder.Core;

namespace Gokko.UI;

/// <summary>LokuOnX の制御API（M17）で操作するパネル: 録音の開始・停止・一時停止・分割、入力のFX・ミュート・ゲイン、出力のオン・音量。未接続のときは灰色。</summary>
public sealed class LokuPanel : Border
{
    private const int Slots = 4;

    private readonly ControlClient _client;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _start = new(), _stop = new(), _pause = new(), _split = new();
    private readonly ToggleButton[] _fx = new ToggleButton[Slots], _mute = new ToggleButton[Slots], _outOn = new ToggleButton[Slots];
    private readonly Slider[] _gain = new Slider[Slots], _vol = new Slider[Slots];
    private readonly TextBlock[] _gainText = new TextBlock[Slots], _volText = new TextBlock[Slots], _outName = new TextBlock[Slots];
    private readonly StackPanel _body = new();
    private bool _syncing;
    private LokuState? _state;

    /// <summary>失敗の理由を画面の下の表示へ出す。</summary>
    public event Action<string>? Error;

    public LokuPanel(ControlClient client)
    {
        _client = client;
        Padding = new Thickness(12);
        Margin = new Thickness(4);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = System.Windows.SystemColors.ControlDarkBrush;

        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = Loc.Get("g.loku.title"), FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(_status);

        _start.Content = Loc.Get("g.loku.start");
        _stop.Content = Loc.Get("g.loku.stop");
        _pause.Content = Loc.Get("g.loku.pause");
        _split.Content = Loc.Get("g.loku.split");
        _start.Click += (_, _) => Cmd("rec.start");
        _stop.Click += (_, _) => Cmd("rec.stop");
        _pause.Click += (_, _) => Cmd(_state?.Paused == true ? "rec.resume" : "rec.pause");
        _split.Click += (_, _) => Cmd("rec.split");
        var rec = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var b in new[] { _start, _stop, _pause, _split })
        {
            b.Padding = new Thickness(10, 4, 10, 4);
            b.Margin = new Thickness(0, 0, 6, 4);
            rec.Children.Add(b);
        }
        _body.Children.Add(rec);

        _body.Children.Add(Header("g.loku.section.in"));
        for (var i = 0; i < Slots; i++) _body.Children.Add(InputRow(i));
        _body.Children.Add(Header("g.loku.section.out"));
        for (var i = 0; i < Slots; i++) _body.Children.Add(OutputRow(i));
        root.Children.Add(_body);
        Child = root;
        Apply(null);
    }

    private static TextBlock Header(string key) =>
        new() { Text = Loc.Get(key), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };

    private Grid InputRow(int i)
    {
        var g = RowGrid();
        g.Children.Add(Cell(new TextBlock { Text = Loc.Get("g.loku.input", i + 1), VerticalAlignment = VerticalAlignment.Center }, 0));
        _fx[i] = new ToggleButton { Content = Loc.Get("g.loku.fx"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = Loc.Get("g.loku.fx.tip", i + 1) };
        _fx[i].Click += (_, _) => { var on = _fx[i].IsChecked == true; Cmd("fx.on", new { target = "input", index = i + 1, on }); };
        g.Children.Add(Cell(_fx[i], 1));
        _mute[i] = new ToggleButton { Content = Loc.Get("g.loku.mute"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = Loc.Get("g.loku.mute.tip", i + 1) };
        _mute[i].Click += (_, _) => Cmd("input.mute", new { index = i + 1, mute = _mute[i].IsChecked == true });
        g.Children.Add(Cell(_mute[i], 2));
        _gain[i] = new Slider { Minimum = ControlDispatcher.MinGainDb, Maximum = ControlDispatcher.MaxGainDb, TickFrequency = 0.5, IsSnapToTickEnabled = true, MinWidth = 90, ToolTip = Loc.Get("g.loku.gain.tip", i + 1), VerticalAlignment = VerticalAlignment.Center };
        _gain[i].ValueChanged += (_, e) => { UpdateText(_gainText[i], e.NewValue); if (!_syncing) Cmd("input.gain", new { index = i + 1, db = e.NewValue }); };
        g.Children.Add(Cell(_gain[i], 3));
        _gainText[i] = new TextBlock { Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(Cell(_gainText[i], 4));
        return g;
    }

    private Grid OutputRow(int i)
    {
        var g = RowGrid();
        _outName[i] = new TextBlock { Text = Loc.Get("g.loku.output", (char)('A' + i)), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(Cell(_outName[i], 0));
        _outOn[i] = new ToggleButton { Content = Loc.Get("g.loku.outon"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = Loc.Get("g.loku.outon.tip", (char)('A' + i)) };
        _outOn[i].Click += (_, _) => Cmd("output.on", new { index = i + 1, on = _outOn[i].IsChecked == true });
        Grid.SetColumnSpan(_outOn[i], 2);
        g.Children.Add(Cell(_outOn[i], 1));
        Grid.SetColumnSpan(_outOn[i], 2);
        _vol[i] = new Slider { Minimum = ControlDispatcher.MinGainDb, Maximum = ControlDispatcher.MaxGainDb, TickFrequency = 0.5, IsSnapToTickEnabled = true, MinWidth = 90, ToolTip = Loc.Get("g.loku.vol.tip", (char)('A' + i)), VerticalAlignment = VerticalAlignment.Center };
        _vol[i].ValueChanged += (_, e) => { UpdateText(_volText[i], e.NewValue); if (!_syncing) Cmd("output.volume", new { index = i + 1, db = e.NewValue }); };
        g.Children.Add(Cell(_vol[i], 3));
        _volText[i] = new TextBlock { Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(Cell(_volText[i], 4));
        return g;
    }

    private static Grid RowGrid()
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return g;
    }

    private static T Cell<T>(T e, int col) where T : UIElement
    {
        Grid.SetColumn(e, col);
        return e;
    }

    private static void UpdateText(TextBlock t, double db) => t.Text = $"{db:0.0} dB";

    private async void Cmd(string cmd, object? args = null)
    {
        if (_syncing) return;
        var r = await _client.Send(cmd, args);
        if (!r.ok) Error?.Invoke(Loc.Get("g.loku.err", r.error));
    }

    /// <summary>状態を画面へ反映する（UI のスレッドで呼ぶ）。null＝未接続。</summary>
    public void Apply(LokuState? s)
    {
        _state = s;
        var on = s != null;
        _body.IsEnabled = on;
        Opacity = on ? 1 : 0.6;
        if (s == null)
        {
            _status.Text = Loc.Get("g.loku.off");
            return;
        }
        var track = s.Track is int t ? Loc.Get("g.loku.track", t) : "";
        _status.Text = Loc.Get("g.loku.on") + " / " + (s.Recording
            ? Loc.Get(s.Paused ? "g.loku.recpause" : "g.loku.rec", track)
            : s.Standby ? Loc.Get("g.loku.standby") : Loc.Get("g.loku.idle"));
        _start.IsEnabled = !s.Recording && !s.Standby;
        _stop.IsEnabled = s.Recording || s.Standby;
        _pause.IsEnabled = s.Recording;
        _pause.Content = Loc.Get(s.Paused ? "g.loku.resume" : "g.loku.pause");
        _split.IsEnabled = s.Recording;

        _syncing = true;
        try
        {
            for (var i = 0; i < Slots; i++)
            {
                var inp = s.Inputs.FirstOrDefault(x => x.Index == i + 1);
                _fx[i].IsEnabled = inp != null && inp.FxAny;
                _fx[i].IsChecked = inp != null && inp.FxAny && inp.FxOn;
                _fx[i].ToolTip = inp != null && !inp.FxAny ? Loc.Get("g.loku.fx.none", i + 1) : Loc.Get("g.loku.fx.tip", i + 1);
                _mute[i].IsEnabled = inp != null && inp.Active;
                _mute[i].IsChecked = inp?.Muted ?? false;
                _gain[i].IsEnabled = inp != null && inp.Active;
                if (inp != null && !_gain[i].IsMouseCaptureWithin) _gain[i].Value = Math.Clamp(inp.GainDb, _gain[i].Minimum, _gain[i].Maximum);
                var outp = s.Outputs.FirstOrDefault(x => x.Index == i + 1);
                _outOn[i].IsEnabled = outp != null;
                _outOn[i].IsChecked = outp?.On ?? false;
                _vol[i].IsEnabled = outp != null;
                if (outp != null)
                {
                    _outName[i].Text = outp.Name.Length > 0 ? outp.Name : Loc.Get("g.loku.output", (char)('A' + i));
                    if (!_vol[i].IsMouseCaptureWithin) _vol[i].Value = Math.Clamp(outp.VolumeDb, _vol[i].Minimum, _vol[i].Maximum);
                }
            }
        }
        finally { _syncing = false; }
    }
}
