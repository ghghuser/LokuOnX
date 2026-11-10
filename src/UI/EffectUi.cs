using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Recorder.UI;

/// <summary>エフェクトウィンドウの部品（カード・スイッチ・スライダーの行）。M11-4。色・見た目はテーマのリソースと既定のスタイルに任せる。</summary>
internal static class EffectUi
{
    public static string Db(double v) => v.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";
    public static string Ms(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " ms";
    public static string Percent(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
    public static string Hz(double v) => v >= 1000
        ? (v / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " kHz"
        : v.ToString("0", CultureInfo.InvariantCulture) + " Hz";

    /// <summary>角丸のカード。header（スイッチか見出し）の下に body を縦に並べる。</summary>
    public static Border Card(UIElement header, out StackPanel body)
    {
        var root = new StackPanel();
        if (header is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(header);
        body = new StackPanel();
        root.Children.Add(body);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = root,
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBg");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        return card;
    }

    /// <summary>効果のオン／オフのスイッチ（丸いトグル）。ユーザーが押したときだけ set を呼ぶ。</summary>
    public static CheckBox Toggle(string text, bool on, string tip, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = on, ToolTip = tip, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        c.Click += (_, _) => set(c.IsChecked == true);
        return c;
    }

    /// <summary>カードの見出し（スイッチの無いもの）。</summary>
    public static TextBlock Title(string text, string tip) =>
        new() { Text = text, ToolTip = tip, FontWeight = FontWeights.SemiBold };

    /// <summary>
    /// ラベル・スライダー・値の1行。ダブルクリックで初期値（def）に戻る。値の表示は text で作る（対数のスライダーは、呼び出し側で変換する）。
    /// step が 0 より大きければ、その刻みに揃う。
    /// </summary>
    public static Grid Slide(string label, double min, double max, double value, double def, Func<double, string> text,
        Action<double> set, double step = 0, string? tip = null, double labelWidth = 96, double valueWidth = 74)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });
        var l = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var v = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var s = new Slider { Minimum = min, Maximum = max, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        if (step > 0)
        {
            s.TickFrequency = step;
            s.IsSnapToTickEnabled = true;
            s.SmallChange = step;
            s.LargeChange = step * 5;
        }
        s.Value = Math.Clamp(value, min, max);
        v.Text = text(s.Value);
        s.ValueChanged += (_, e) =>
        {
            v.Text = text(e.NewValue);
            set(e.NewValue);
        };
        s.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            s.Value = def;
            e.Handled = true;
        };
        var tooltip = (tip == null ? "" : tip + "\n") + "ダブルクリックで初期値に戻ります";
        l.ToolTip = s.ToolTip = tooltip;
        Grid.SetColumn(s, 1);
        Grid.SetColumn(v, 2);
        g.Children.Add(l);
        g.Children.Add(s);
        g.Children.Add(v);
        return g;
    }
}
