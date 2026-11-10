using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// プルダウンの項目を「注意が出る選択肢」として赤系の文字にする（選ぶこと自体は可能）。
/// values[0]＝項目のキー（入力は Key、出力先は Id）、values[1]＝そのプルダウンの Tag（注意が出るキーの HashSet）。
/// 注意が出ない項目は UnsetValue を返し、いつもの文字色のままにする。
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
        if (values.Length >= 2 && values[0] is string key && values[1] is HashSet<string> warn && warn.Contains(key))
            return Red;
        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
