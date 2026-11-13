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
        eqToggle = EffectUi.Toggle(Loc.Get("effecteq.s01"), fx.EqOn,
            Loc.Get("effecteq.s02"),
            on => { fx.EqOn = on; Curve(); changed(); });
        // 見出しの行: 左にチェック、右にプルダウン（よく使う設定）と「↺」（フラットに戻す）
        var presets = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 6, 0), ToolTip = Loc.Get("effecteq.s03") };
        foreach (var p in EqBands.Presets) presets.Items.Add(p.Name);
        var reset = EffectUi.ResetButton(Loc.Get("effecteq.s04"), () => Flat());
        var head = EffectUi.Head(eqToggle, reset, presets);
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
        paramToggle = EffectUi.Toggle(Loc.Get("effecteq.s05"), fx.ParamOn,
            Loc.Get("effecteq.s06"),
            on => { fx.ParamOn = on; view.SetParams(on ? fx.Params : null); changed(); });
        var prs = EffectUi.ResetButton(Loc.Get("effecteq.s07"), () =>
        {
            fx.Params = ParamBand.Defaults();
            changed();
            rebuild();
        });
        panel.Children.Add(EffectUi.Card(EffectUi.Head(paramToggle, prs), out var pe));
        pe.Children.Add(ParamHead());
        for (int i = 0; i < ParamBand.Count; i++) pe.Children.Add(ParamRow(fx, i, view, paramToggle, changed));

        // ---- ローカット（チェックで入／切。切にしても周波数は覚えていて、入にすると戻る） ----
        double lastHz = fx.HpfHz >= 20 ? fx.HpfHz : 80;
        CheckBox lcToggle = null!;
        Grid lcRow = null!;
        Slider lcSlider = null!;
        lcToggle = EffectUi.Toggle(Loc.Get("effecteq.s08"), fx.HpfHz >= 20,
            Loc.Get("effecteq.s09"),
            on => lcSlider.Value = on ? lastHz : 0);
        var lcReset = EffectUi.ResetButton(Loc.Get("effecteq.s10"), () =>
        {
            lastHz = 80;
            if (fx.HpfHz >= 20) lcSlider.Value = 80;
        });
        panel.Children.Add(EffectUi.Card(EffectUi.Head(lcToggle, lcReset), out var lc));
        lcRow = EffectUi.Slide(Loc.Get("effecteq.s11"), 0, 400, fx.HpfHz, 0, v => v < 20 ? Loc.Get("effecteq.s12") : v.ToString("0") + " Hz",
            v =>
            {
                fx.HpfHz = v < 20 ? 0 : v;
                if (v >= 20) lastHz = v;
                lcToggle.IsChecked = v >= 20;
                lcRow.Opacity = v >= 20 ? 1.0 : 0.55;
                view.SetHpf(fx.HpfHz);
                changed();
            }, 5, Loc.Get("effecteq.s13"));
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
        Add(Loc.Get("effecteq.s11"), 1);
        Add(Loc.Get("effecteq.s14"), 3);
        Add(Loc.Get("effecteq.s15"), 5);
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
            v => { fx.Params[i].Freq = ParamBand.MinHz * Math.Pow(2, v / 12); Touch(); }, 1, Loc.Get("effecteq.s16"));
        EffectUi.Mini(g, 3, -ParamBand.MaxDb, ParamBand.MaxDb, pb.Db, 0, EffectUi.Db,
            v =>
            {
                fx.Params[i].Db = v;
                if (!fx.ParamOn && Math.Abs(v) > 0.01) { fx.ParamOn = true; toggle.IsChecked = true; }   // 動かしたら自動でオン
                Touch();
            }, 0.5, Loc.Get("effecteq.s17"));
        EffectUi.Mini(g, 5, ParamBand.MinQ, ParamBand.MaxQ, pb.Q, ParamBand.DefaultQ, v => "Q " + v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            v => { fx.Params[i].Q = v; Touch(); }, 0.1, Loc.Get("effecteq.s18"));
        return g;
    }
}
