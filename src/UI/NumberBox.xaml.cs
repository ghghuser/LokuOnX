using System.Windows;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>数字だけの入力欄。右端の ▲▼ で ±1。IMEを切って半角で入力させ、貼り付けた全角数字は半角へ直す。</summary>
public partial class NumberBox : System.Windows.Controls.UserControl
{
    public int Minimum { get; set; } = 0;
    public int Maximum { get; set; } = int.MaxValue;

    public NumberBox()
    {
        InitializeComponent();
        InputMethod.SetIsInputMethodEnabled(Box, false);
        Box.TextChanged += (_, _) =>
        {
            var half = Digits.ToHalfWidth(Box.Text);
            if (half == Box.Text) return;
            var caret = Box.CaretIndex;
            Box.Text = half;
            Box.CaretIndex = Math.Min(caret, half.Length);
        };
    }

    public string Text
    {
        get => Box.Text;
        set => Box.Text = value;
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Step(+1);
    private void Down_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void Step(int delta)
    {
        if (!int.TryParse(Digits.ToHalfWidth(Box.Text).Trim(), out var n)) n = Minimum;
        n = (int)Math.Clamp((long)n + delta, Minimum, Maximum);
        Box.Text = n.ToString();
        // 入力欄から離れたときと同じ処理（設定への反映）を呼ぶ
        RaiseEvent(new RoutedEventArgs(LostFocusEvent, this));
    }
}
