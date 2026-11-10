using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>プルダウンの Tag に入れる、注意のキーの集合。Red＝赤（使えない・エラーになる）、Yellow＝黄（使えるが注意）。</summary>
public sealed record WarnSet(HashSet<string> Red, HashSet<string>? Yellow = null);

/// <summary>
/// プルダウンの項目を「注意が出る選択肢」として赤系の文字にする（選ぶこと自体は可能）。
/// values[0]＝項目のキー（入力は Key、出力先は Id）、values[1]＝そのプルダウンの Tag（注意が出るキーの集合 WarnSet。赤＝エラー、黄＝注意）、values[2]・[3]＝普通の項目に使う文字色（ComboBoxItem・ComboBox の Foreground）。
/// 注意が出ない項目は、周りの文字色をそのまま返す。
/// </summary>
public sealed class WarnBrushConverter : IMultiValueConverter
{
    private static readonly Brush Red = Make();

    private static Brush Make()
    {
        var b = new SolidColorBrush(Color.FromRgb(0xE5, 0x57, 0x57));
        b.Freeze();
        return b;
    }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string key && values[1] is WarnSet warn)
        {
            if (warn.Red.Contains(key)) return Red;
            if (warn.Yellow != null && warn.Yellow.Contains(key) && Application.Current?.TryFindResource("CautionFg") is Brush y) return y;
        }
        // 通常の項目は、周りと同じ文字色（一覧の中は ComboBoxItem、選択中の表示は ComboBox の文字色）をそのまま返す。
        // UnsetValue を返すと、ダークテーマで黒になってしまう
        for (int i = 2; i < values.Length; i++)
            if (values[i] is Brush b) return b;
        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
