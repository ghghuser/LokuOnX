using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// デバイス一覧の先頭の絵文字（🎤・🔊・🎛）に付ける色を返す。色は Theme/Common.xaml の IconMicFg・IconSpeakerFg・IconAsioFg（ここを書き換えれば変わる）。
/// </summary>
public sealed class DeviceIconBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string switch
        {
            "🎤" => "IconMicFg",
            "🔊" => "IconSpeakerFg",
            "🎛" => "IconAsioFg",
            _ => "WindowFg",
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
