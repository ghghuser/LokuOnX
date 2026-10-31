using System.Globalization;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>「現在の設定」サマリー行の文字列を作る（PROJECT.md §3-6）。折りたたみの外に常時表示する。</summary>
public static class SummaryBar
{
    public static string Build(Settings s, AudioDeviceInfo? dev)
    {
        var input = dev?.Label ?? "（未選択）";
        var parts = new List<string>
        {
            $"入力: {input}",
            Quality(s, dev),
            $"保存先 {RecordingPlan.PreviewFolder(s, DateTime.Now)}",
            $"名前 {NameLabel(s)}",
            $"No.{s.NextNo}",
        };
        var text = string.Join(" ／ ", parts);
        return s.ReadOnly ? text + " ／ ※設定は保存されません" : text;
    }

    /// <summary>実際に書き出される形式・音質（デバイスの値を設定値で置き換えたあと）。</summary>
    private static string Quality(Settings s, AudioDeviceInfo? dev)
    {
        var o = s.Encode;
        var (rate, ch) = o.Resolve(dev?.SampleRate ?? 0, dev?.Channels ?? 0);
        var parts = new List<string> { o.Fmt.Label };
        if (o.Fmt.Lossy) parts.Add($"{o.BitrateKbps}kbps");
        if (rate > 0) parts.Add((rate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "kHz");
        if (ch > 0) parts.Add(ch switch { 1 => "モノ", 2 => "ステレオ", var n => $"{n}ch" });
        return string.Join(" ", parts);
    }

    /// <summary>使っているパターンに一致するテンプレート名。どれにも一致しなければ「カスタム」。</summary>
    private static string NameLabel(Settings s)
    {
        var list = s.Templates;
        var hit = list.FirstOrDefault(t => t.Name == s.Template && t.Pattern == s.Pattern)
                  ?? list.FirstOrDefault(t => t.Pattern == s.Pattern);
        return hit?.Name ?? "カスタム";
    }
}
