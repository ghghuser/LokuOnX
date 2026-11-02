using System.Windows;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 時:分:秒（00:00:00）の入力欄。右端の ▲▼ は、入力欄のカーソル（インジケーター）がある桁（時・分・秒）を ±1 する。
/// 入力欄をまだ触っていないときは「分」。↑↓キーでも同じ。IMEを切って半角で入力させる。
/// </summary>
public partial class TimeBox : System.Windows.Controls.UserControl
{
    private bool _touched;

    public TimeBox()
    {
        InitializeComponent();
        InputMethod.SetIsInputMethodEnabled(Box, false);
        Box.TextChanged += (_, _) =>
        {
            var fixedText = Digits.ToHalfWidth(Box.Text).Replace('：', ':');
            if (fixedText == Box.Text) return;
            var caret = Box.CaretIndex;
            Box.Text = fixedText;
            Box.CaretIndex = Math.Min(caret, fixedText.Length);
        };
    }

    public string Text
    {
        get => Box.Text;
        set => Box.Text = value;
    }

    private void Box_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _touched = true;

    private void Box_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var c in e.Text)
            if ((c < '0' || c > '9') && c != ':' && c != '：') { e.Handled = true; return; }
    }

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up) { Step(+1); e.Handled = true; }
        else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Step(+1);
    private void Down_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void Step(int delta)
    {
        SplitTime.TryParse(Box.Text, out var cur);   // 読めなければ 0
        // 桁: 00:00:00 の 0〜2 文字目=時、3〜5=分、6〜=秒。形が崩れている・まだ触っていないときは「分」
        int seg = 1;
        if (_touched && Box.Text.Length == 8 && Box.Text[2] == ':' && Box.Text[5] == ':')
        {
            int c = Box.CaretIndex;
            seg = c <= 2 ? 0 : c <= 5 ? 1 : 2;
        }
        long unit = seg == 0 ? 3600 : seg == 1 ? 60 : 1;
        long next = cur + delta * unit;
        int v = SplitTime.Clamp(next);
        if (next > 0 && next < SplitTime.Min) v = delta > 0 ? SplitTime.Min : 0;   // 1〜9秒は使えない
        Box.Text = SplitTime.Format(v);
        Box.Select(seg * 3, 2);   // 動かした桁を選択状態にして示す
        RaiseEvent(new RoutedEventArgs(LostFocusEvent, this));   // 設定への反映（入力欄から離れたときと同じ処理）
    }
}
