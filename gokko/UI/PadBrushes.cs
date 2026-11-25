using System.Windows.Media;

namespace Gokko.UI;

/// <summary>パッドの色（8色）。</summary>
public static class PadBrushes
{
    private static readonly string[] Hex = { "#3A7BD5", "#2FA36B", "#D9822B", "#C8453D", "#8E5BD1", "#1FA7B8", "#C2A21E", "#D0558F" };

    private static Color Col(int i) => (Color)ColorConverter.ConvertFromString(Hex[((i % Hex.Length) + Hex.Length) % Hex.Length]);

    public static Brush Fill(int i)
    {
        var b = new SolidColorBrush(Col(i));
        b.Freeze();
        return b;
    }

    /// <summary>鳴っているとき用の明るい色。</summary>
    public static Brush Lit(int i)
    {
        var c = Col(i);
        var b = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, c.R + 70), (byte)Math.Min(255, c.G + 70), (byte)Math.Min(255, c.B + 70)));
        b.Freeze();
        return b;
    }
}
