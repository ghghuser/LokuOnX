using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>角丸の切り抜き。値は 幅・高さ、ConverterParameter は角の半径。中身（四角い見出し行など）が丸い角からはみ出さないようにする。</summary>
public sealed class RoundClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double w || values[1] is not double h || w <= 0 || h <= 0) return Geometry.Empty;
        var r = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 8;
        return new RectangleGeometry(new Rect(0, 0, w, h), r, r);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
