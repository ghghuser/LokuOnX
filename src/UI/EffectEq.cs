using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// エフェクトウィンドウの「イコライザ」タブ（M11-4確認後）。上から グラフィックEQ（つまみ＋周波数カーブ）→ パラメトリックEQ → ローカット（画面の並びで、音の掛かる順番とは別）。
/// カーブはローカット・パラメトリックの効果も含めた全体を表す（LokuOn と同じ）。fx を直接書き換え、そのたびに changed を呼ぶ。
/// </summary>
internal static class EffectEq
{
    /// <param name="rebuild">パラメトリックを初期値に戻したあとなど、画面の作り直しを頼む（EffectWindow が受ける）。</param>
    public static StackPanel Build(EffectSetting fx, Action changed, Action rebuild)
    {
        var panel = new StackPanel();
        var view = new GraphicEqControl();
        CheckBox eqToggle = null!, paramToggle = null!;
        bool loading = true;

        void Curve()
        {
            view.SetBands(fx.Bands);
            view.Dimmed = !fx.EqOn;
            view.SetHpf(fx.HpfHz);
            view.SetParams(fx.ParamOn ? fx.Params : null);
        }

        // ---- グラフィックEQ ----
        eqToggle = EffectUi.Toggle("グラフィックEQ（10バンド）", fx.EqOn,
            "帯域ごとに音量を上げ下げします（各 ±12 dB）。ローカットの後に掛かります。グラフの曲線は、ローカット・パラメトリックEQも含めた全体の特性です",
            on => { fx.EqOn = on; Curve(); changed(); });
        // 見出しの行: チェック → よく使う設定のプルダウン（左詰め）…右端に「フラットに戻す」。行を節約する
        var head = new DockPanel { LastChildFill = false };
        var reset = new Button { Content = "フラットに戻す", Padding = new Thickness(10, 2, 10, 2) };
        DockPanel.SetDock(reset, Dock.Right);
        head.Children.Add(reset);
        DockPanel.SetDock(eqToggle, Dock.Left);
        head.Children.Add(eqToggle);
        var presets = new ComboBox { MinWidth = 170, Margin = new Thickness(14, 0, 0, 0), ToolTip = "よく使う設定を選ぶと、10個のつまみがその値になります（選んだあとも自由に動かせます）" };
        foreach (var p in EqBands.Presets) presets.Items.Add(p.Name);
        DockPanel.SetDock(presets, Dock.Left);
        head.Children.Add(presets);
        panel.Children.Add(EffectUi.Card(head, out var g));
        g.Children.Add(view);

        void ShowPreset() { loading = true; presets.SelectedIndex = MatchPreset(fx.Bands); loading = false; }
        void Flat()
        {
            Array.Clear(fx.Bands, 0, fx.Bands.Length);
            Curve();
            ShowPreset();
            changed();
        }
        reset.Click += (_, _) => Flat();
        presets.SelectionChanged += (_, _) =>
        {
            int i = presets.SelectedIndex;
            if (loading || i < 0 || i >= EqBands.Presets.Length) return;
            Array.Copy(EqBands.Presets[i].Db, fx.Bands, EqBands.Count);
            if (i > 0 && !fx.EqOn) { fx.EqOn = true; eqToggle.IsChecked = true; }
            Curve();
            changed();
        };
        view.BandsChanged += () =>
        {
            if (loading) return;
            Array.Copy(view.GetBands(), fx.Bands, EqBands.Count);
            if (!fx.EqOn && fx.Bands.Any(v => v != 0)) { fx.EqOn = true; eqToggle.IsChecked = true; }
            view.Dimmed = !fx.EqOn;
            view.SetHpf(fx.HpfHz);
            view.SetParams(fx.ParamOn ? fx.Params : null);
            loading = true; presets.SelectedIndex = MatchPreset(fx.Bands); loading = false;
            changed();
        };

        // ---- パラメトリックEQ ----
        paramToggle = EffectUi.Toggle("パラメトリックEQ（4バンド）", fx.ParamOn,
            "位置（周波数）・上げ下げ（ゲイン）・幅（Q。大きいほど狭い）を自由に決めて、気になる音だけを調整します。グラフィックEQの後に掛かります",
            on => { fx.ParamOn = on; view.SetParams(on ? fx.Params : null); changed(); });
        panel.Children.Add(EffectUi.Card(paramToggle, out var pe));
        pe.Children.Add(ParamHead());
        for (int i = 0; i < ParamBand.Count; i++) pe.Children.Add(ParamRow(fx, i, view, paramToggle, changed));
        var prs = new Button { Content = "パラメトリックを初期値に戻す", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        prs.Click += (_, _) =>
        {
            fx.Params = ParamBand.Defaults();
            fx.ParamOn = false;
            paramToggle.IsChecked = false;
            changed();
            rebuild();
        };
        pe.Children.Add(prs);

        // ---- ローカット（チェックで入／切。切にしても周波数は覚えていて、入にすると戻る） ----
        double lastHz = fx.HpfHz >= 20 ? fx.HpfHz : 80;
        CheckBox lcToggle = null!;
        Grid lcRow = null!;
        Slider lcSlider = null!;
        lcToggle = EffectUi.Toggle("ローカット", fx.HpfHz >= 20,
            "この周波数より低い音（風・机の振動・エアコンなど）を削ります。切にしても周波数は覚えています。曲線にも反映されます",
            on => lcSlider.Value = on ? lastHz : 0);
        panel.Children.Add(EffectUi.Card(lcToggle, out var lc));
        lcRow = EffectUi.Slide("周波数", 0, 400, fx.HpfHz, 0, v => v < 20 ? "なし" : v.ToString("0") + " Hz",
            v =>
            {
                fx.HpfHz = v < 20 ? 0 : v;
                if (v >= 20) lastHz = v;
                lcToggle.IsChecked = v >= 20;
                lcRow.Opacity = v >= 20 ? 1.0 : 0.55;
                view.SetHpf(fx.HpfHz);
                changed();
            }, 5, "左端付近＝切（「なし」）。20〜400 Hz（12 dB/oct）");
        lcSlider = lcRow.Children.OfType<Slider>().First();
        lcRow.Opacity = fx.HpfHz >= 20 ? 1.0 : 0.55;
        lc.Children.Add(lcRow);

        Curve();
        ShowPreset();
        loading = false;
        return panel;
    }

    private static int MatchPreset(double[] b)
    {
        for (int i = 0; i < EqBands.Presets.Length; i++)
        {
            var d = EqBands.Presets[i].Db;
            bool same = true;
            for (int k = 0; k < EqBands.Count && same; k++) same = Math.Abs(b[k] - d[k]) < 0.01;
            if (same) return i;
        }
        return -1;
    }

    private static void Cols(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        for (int k = 0; k < 3; k++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        }
    }

    private static Grid ParamHead()
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2), Opacity = 0.7 };
        Cols(g);
        void Add(string t, int col)
        {
            var tb = new TextBlock { Text = t, FontSize = 10 };
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }
        Add("周波数", 1);
        Add("ゲイン", 3);
        Add("幅（大きいほど狭い）", 5);
        return g;
    }

    /// <summary>1バンドを1行に（名前｜周波数｜ゲイン｜幅）。周波数は 20 Hz から半音（1/12オクターブ）刻み。</summary>
    private static Grid ParamRow(EffectSetting fx, int i, GraphicEqControl view, CheckBox toggle, Action changed)
    {
        var pb = fx.Params[i];
        var g = new Grid { Height = 26 };
        Cols(g);
        g.Children.Add(new TextBlock { Text = ParamBand.Names[i], FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        double Semis(double hz) => 12 * Math.Log2(Math.Max(ParamBand.MinHz, hz) / ParamBand.MinHz);
        void Touch() { view.SetParams(fx.ParamOn ? fx.Params : null); changed(); }
        EffectUi.Mini(g, 1, 0, 120, Semis(pb.Freq), Semis(ParamBand.DefaultFreqs[i]), v => EffectUi.Hz(ParamBand.MinHz * Math.Pow(2, v / 12)),
            v => { fx.Params[i].Freq = ParamBand.MinHz * Math.Pow(2, v / 12); Touch(); }, 1, "気になる音の高さ（20 Hz〜20 kHz）");
        EffectUi.Mini(g, 3, -ParamBand.MaxDb, ParamBand.MaxDb, pb.Db, 0, EffectUi.Db,
            v =>
            {
                fx.Params[i].Db = v;
                if (!fx.ParamOn && Math.Abs(v) > 0.01) { fx.ParamOn = true; toggle.IsChecked = true; }   // 動かしたら自動でオン
                Touch();
            }, 0.5, "上げる／下げる量（0 dB＝効果なし）");
        EffectUi.Mini(g, 5, ParamBand.MinQ, ParamBand.MaxQ, pb.Q, ParamBand.DefaultQ, v => "Q " + v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            v => { fx.Params[i].Q = v; Touch(); }, 0.1, "大きいほど狭い範囲だけに効きます");
        return g;
    }
}
