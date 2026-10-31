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
            $"{s.Format} {Quality(dev)}".TrimEnd(),
            $"保存先 {s.Folder}",
            "名前 日時形式",
            $"No.{s.NextNo}",
        };
        var text = string.Join(" ／ ", parts);
        return s.ReadOnly ? text + " ／ ※設定は保存されません" : text;
    }

    private static string Quality(AudioDeviceInfo? dev)
    {
        if (dev == null || dev.SampleRate <= 0) return "";
        var khz = (dev.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);
        var ch = dev.Channels switch { 1 => "モノ", 2 => "ステレオ", var n => $"{n}ch" };
        return $"{khz}kHz {ch}";
    }
}
