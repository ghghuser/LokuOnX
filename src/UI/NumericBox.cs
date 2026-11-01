using System.Windows.Controls;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>数字だけの入力欄用。IMEを切って半角で入力させ、貼り付けられた全角数字は半角へ直す。</summary>
public static class NumericBox
{
    public static void Attach(TextBox box)
    {
        InputMethod.SetIsInputMethodEnabled(box, false);
        box.TextChanged += (_, _) =>
        {
            var half = Digits.ToHalfWidth(box.Text);
            if (half == box.Text) return;
            var caret = box.CaretIndex;
            box.Text = half;
            box.CaretIndex = Math.Min(caret, half.Length);
        };
    }
}
