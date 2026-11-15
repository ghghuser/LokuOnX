using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// 見出し付きの枠（GroupFrame）の切り抜き。枠全体の四角から、見出しの文字の四角を除く。
/// 値の並び: 枠の幅・枠の高さ・見出しの幅・見出しの高さ。見出しは枠の左から 12px、枠の上端より 9px 上から始まる（Common.xaml の GroupFrame）。
/// </summary>
public sealed class GroupGapClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values.Any(v => v is not double d || double.IsNaN(d))) return Geometry.Empty;
        double w = (double)values[0], h = (double)values[1], lw = (double)values[2], lh = (double)values[3];
        if (w <= 0 || h <= 0) return Geometry.Empty;
        return new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, w, h)),
            new RectangleGeometry(new Rect(12, -9, lw, lh)));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
