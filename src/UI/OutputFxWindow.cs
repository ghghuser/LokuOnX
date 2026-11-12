using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 出力ごとのエフェクトのウィンドウ（M11-5）。出力カードの「FX」ボタンから開く（出力1枚につき1つ）。
/// この出力に流れる最終ミックス（全体音量のあと）にだけ掛かる。掛かる順番: ローカット → グラフィックEQ → パラメトリックEQ → リミッター。
/// イコライザの画面は入力のエフェクトと同じ部品（EffectEq）を使う。設定は host.Update でその出力へ即反映し、設定ファイルへは少し遅れて保存する。
/// </summary>
public sealed class OutputFxWindow : Window
{
    private readonly IOutputHost _host;
    private readonly int _index;
    private readonly ContentControl _body = new();
    private TextBlock _summary = new();   // 画面を作り直すたびに新しく作る（古い画面に付いたままだと「already the logical child」で落ちる）
    private readonly Settings _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private OutputFxSetting _fx = new();
    private bool _building;

    public int Index => _index;

    public OutputFxWindow(IOutputHost host, Settings settings, int index)
    {
        _host = host;
        _s = settings;
        _index = index;
        Width = 680;
        Height = 780;
        MinWidth = 560;
        MinHeight = 460;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "WindowFg");
        EffectPalette.Apply(this);
        _body.Margin = new Thickness(16, 12, 16, 12);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body };
        ThemeManager.Attach(this, settings.Theme);
        RestoreRect();
        Load();
        _timer.Tick += (_, _) => CheckAlive();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); SaveRect(); };
    }

    // ---- 位置と大きさ（閉じるときに保存し、次に開くときに復元する。どの出力でも共通） ----
    private void RestoreRect()
    {
        var parts = _s.OutputFxWindowRect.Split(',');
        if (parts.Length != 4) return;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return;
        double sl = SystemParameters.VirtualScreenLeft, st = SystemParameters.VirtualScreenTop;
        double sw = SystemParameters.VirtualScreenWidth, sh = SystemParameters.VirtualScreenHeight;
        if (v[2] < MinWidth || v[3] < MinHeight || v[0] + 80 > sl + sw || v[0] + v[2] - 80 < sl || v[1] + 40 > st + sh || v[1] < st - 10) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = v[0]; Top = v[1]; Width = v[2]; Height = v[3];
    }

    private void SaveRect()
    {
        if (WindowState != WindowState.Normal) return;
        _s.OutputFxWindowRect = string.Join(",", new[] { Left, Top, Width, Height }.Select(d => Math.Round(d).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>出力が減った・入れ替わったとき（カードの削除・プリセットの読み込み）は閉じる。名前の変更はタイトルへ反映する。</summary>
    private void CheckAlive()
    {
        if (_index >= _host.Configs.Count) { Close(); return; }
        Title = $"出力のエフェクト「{_host.Configs[_index].Name}」";
    }

    /// <summary>設定を読み直して画面を作り直す（「すべて初期値に」やパラメトリックの初期化のあと）。</summary>
    private void Load()
    {
        if (_index >= _host.Configs.Count) return;
        _building = true;
        _fx = _host.Configs[_index].FxCopy();
        _body.Content = BuildBody();
        CheckAlive();
        UpdateSummary();
        _building = false;
    }

    private void Commit()
    {
        if (_building || _index >= _host.Configs.Count) return;
        _fx.Normalize();
        _host.Update(_index, _host.Configs[_index] with { Fx = _fx.Clone() });
        UpdateSummary();
    }

    /// <summary>「かかっているもの」の一行。入力のエフェクトと同じ「⚡＋頭文字」を、入力ランプと同じ緑で出す（バイパス中は灰色）。
    /// 頭文字は掛かる順番で L＝ローカット G＝グラフィックEQ P＝パラメトリックEQ M＝リミッター（例 ⚡LGM）。</summary>
    private void UpdateSummary()
    {
        var letters = new string(new[] { (_fx.HpfHz >= 20, 'L'), (_fx.EqOn, 'G'), (_fx.ParamOn, 'P'), (_fx.LimiterOn, 'M') }
            .Where(x => x.Item1).Select(x => x.Item2).ToArray());
        _summary.Inlines.Clear();
        _summary.Inlines.Add(new System.Windows.Documents.Run("かかっているもの: "));
        if (letters.Length == 0)
            _summary.Inlines.Add(new System.Windows.Documents.Run("なし"));
        else
        {
            var mark = new System.Windows.Documents.Run("⚡" + letters) { FontWeight = FontWeights.SemiBold };
            mark.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, _fx.Bypass ? "DisabledFg" : "LampOn");
            _summary.Inlines.Add(mark);
            if (_fx.Bypass) _summary.Inlines.Add(new System.Windows.Documents.Run("（バイパス中。設定は残っています）") { FontSize = 11, Opacity = 0.7 });
        }
        _summary.ToolTip = "L＝ローカット　G＝グラフィックEQ　P＝パラメトリックEQ　M＝リミッター";
    }

    private UIElement BuildBody()
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "この出力に流れる最終ミックス（全体音量のあと）にだけ掛かります。入力ごとのエフェクトとは別で、録音には影響しません。",
            FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
        });
        _summary = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        root.Children.Add(_summary);

        var bypass = new CheckBox { Content = "すべてバイパス", IsChecked = _fx.Bypass, Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "設定は残したまま、効果だけを一時的に切ります" };
        bypass.Click += (_, _) => { _fx.Bypass = bypass.IsChecked == true; Commit(); };
        root.Children.Add(bypass);
        var reset = new Button { Content = "すべて初期値に", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "この出力のエフェクトの設定をすべて初期値（何も掛けない）に戻します" };
        reset.Click += (_, _) => { _fx = new OutputFxSetting(); Commit(); Load(); };
        root.Children.Add(reset);

        // イコライザ（入力のエフェクトと同じ画面部品。ローカット・EQの項目だけを写して使い、変わったら出力の設定へ戻す）
        var eq = _fx.ToEqSetting();
        Action fromEq = () =>
        {
            _fx.EqOn = eq.EqOn;
            _fx.HpfHz = eq.HpfHz;
            _fx.Bands = (double[])eq.Bands.Clone();
            _fx.ParamOn = eq.ParamOn;
            _fx.Params = eq.Params.Select(p => p.Clone()).ToArray();
            Commit();
        };
        root.Children.Add(EffectEq.Build(eq, fromEq, Load));

        root.Children.Add(BuildLimiter());
        root.Children.Add(new TextBlock
        {
            Text = "掛かる順番: 全体音量 → ローカット → グラフィックEQ → パラメトリックEQ → リミッター。リミッターは1.5ミリ秒先読みするので、この出力だけわずかに遅れます。つまみのダブルクリックで初期値に戻ります。",
            FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        return root;
    }

    private Border BuildLimiter()
    {
        var toggle = EffectUi.Toggle("リミッター", _fx.LimiterOn,
            "音の大きすぎる部分を、割れないように自動で下げます。上限を超えない音量に収めます（最後に掛かります）",
            on => { _fx.LimiterOn = on; Commit(); });
        var d = new OutputFxSetting();
        var head = EffectUi.Head(toggle, EffectUi.ResetButton("リミッターのつまみを初期値に戻します（入／切はそのまま）", () =>
        {
            _fx.CeilingDb = d.CeilingDb;
            _fx.ReleaseMs = d.ReleaseMs;
            Commit();
            Load();
        }));
        var card = EffectUi.Card(head, out var body);
        body.Children.Add(EffectUi.Slide("上限", OutputFxSetting.CeilingMinDb, OutputFxSetting.CeilingMaxDb, _fx.CeilingDb, d.CeilingDb, EffectUi.Db,
            v => { _fx.CeilingDb = v; Commit(); }, 0.5, "この音量を超えないように下げます（0 dB＝割れる寸前）"));
        body.Children.Add(EffectUi.Slide("戻り時間", OutputFxSetting.ReleaseMinMs, OutputFxSetting.ReleaseMaxMs, _fx.ReleaseMs, d.ReleaseMs, EffectUi.Ms,
            v => { _fx.ReleaseMs = v; Commit(); }, 10, "下げたあと、元の音量に戻るまでの時間"));
        return card;
    }
}
