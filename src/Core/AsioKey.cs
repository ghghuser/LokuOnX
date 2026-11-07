namespace Recorder.Core;

/// <summary>
/// ASIO入力の Key の約束事（M8）。形式は "asio:{ドライバ名}|{最初のチャンネル(0始まり)}|{チャンネル数}"。
/// 例: asio:FlexASIO|0|2（入力1-2）、asio:FlexASIO|1|1（入力2）。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public static class AsioKey
{
    public const string Prefix = "asio:";

    public static bool Is(string key) => key.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Make(string driver, int firstChannel, int channels) =>
        $"{Prefix}{driver}|{firstChannel}|{channels}";

    /// <summary>Key を分解する。ドライバ名に「|」が含まれていても、後ろの2つの数字から切り分ける。</summary>
    public static bool TryParse(string key, out string driver, out int firstChannel, out int channels)
    {
        driver = "";
        firstChannel = 0;
        channels = 0;
        if (!Is(key)) return false;
        var rest = key.Substring(Prefix.Length);
        int b = rest.LastIndexOf('|');
        if (b < 1) return false;
        int a = rest.LastIndexOf('|', b - 1);
        if (a < 1) return false;
        if (!int.TryParse(rest.AsSpan(a + 1, b - a - 1), out firstChannel)) return false;
        if (!int.TryParse(rest.AsSpan(b + 1), out channels)) return false;
        driver = rest.Substring(0, a);
        return firstChannel >= 0 && channels >= 1 && driver.Length > 0;
    }

    /// <summary>「1-2」「3」のような、人が読むチャンネル番号（1始まり）。</summary>
    public static string ChannelText(int firstChannel, int channels) =>
        channels <= 1 ? (firstChannel + 1).ToString() : $"{firstChannel + 1}-{firstChannel + channels}";

    /// <summary>「FlexASIO 入力1-2」のような短い説明。分解できなければ Key をそのまま返す。</summary>
    public static string Describe(string key) =>
        TryParse(key, out var driver, out var first, out var count)
            ? $"{driver} 入力{ChannelText(first, count)}"
            : key;
}
