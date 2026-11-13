using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Recorder.Core;

/// <summary>名前に使う値。Start / End は録音全体の0:00起点の経過時間（整数秒）。</summary>
public sealed record NameContext(DateTime Time, int No, int Track, TimeSpan Start, TimeSpan End,
    string Device, string Profile, string Ext)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// ファイル名・フォルダ名の生成を1か所に集約する（PROJECT.md §3-4, §5-10）。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public static class FileNamer
{
    public const string DefaultPattern = "{Date} {Time} (No.{No}).{ext}";
    public const string DefaultDateFormat = "{yyyy}-{MM}-{dd}";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex Var = new(@"\{([A-Za-z]+)(?::([A-Za-z]+))?\}", RegexOptions.Compiled);
    private static readonly Regex Reserved = new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>テンプレート中の {変数} を値に置き換える。知らない変数はそのまま残す。</summary>
    public static string Expand(string template, NameContext c) =>
        Var.Replace(template, m => Value(m.Groups[1].Value, m.Groups[2].Value, c) ?? m.Value);

    private static string? Value(string name, string opt, NameContext c)
    {
        var t = c.Time;
        return name switch
        {
            "yyyy" => t.ToString("yyyy", Inv),
            "yy" => t.ToString("yy", Inv),
            "MM" => t.ToString("MM", Inv),
            "dd" => t.ToString("dd", Inv),
            "HH" => t.ToString("HH", Inv),
            "mm" => t.ToString("mm", Inv),
            "ss" => t.ToString("ss", Inv),
            "Date" => t.ToString("yyyy_MM_dd", Inv),
            "Time" => t.ToString("HH_mm_ss", Inv),
            "No" => c.No.ToString(Inv),
            "Track" => c.Track.ToString("000", Inv),
            "Start" => Clock(c.Start, opt, false),
            "End" => Clock(c.End, opt, false),
            "Length" => Clock(c.Length, opt, true),
            "Device" => c.Device,
            "Profile" or "Preset" => c.Profile,   // {Preset} は旧名（以前のファイル名パターンが動くように残す）
            "ext" or "Ext" => c.Ext,
            _ => null,
        };
    }

    /// <summary>
    /// 時間表記（コロンは全角）。1時間以上は h：mm：ss。未満は Start/End が mm：ss、Length が m：ss。
    /// 「:min」を付けると 125：30 のように分だけで表す。
    /// </summary>
    private static string Clock(TimeSpan ts, string opt, bool shortMinutes)
    {
        long total = Math.Max(0, (long)ts.TotalSeconds);
        long h = total / 3600, m = total / 60 % 60, s = total % 60;
        if (opt == "min") return $"{total / 60}：{s:00}";
        if (h > 0) return $"{h}：{m:00}：{s:00}";
        return shortMinutes ? $"{m}：{s:00}" : $"{m:00}：{s:00}";
    }

    /// <summary>テンプレートから、禁止文字を置換した最終のファイル名（拡張子つき）を作る。</summary>
    public static string FileName(string template, NameContext c)
    {
        var pattern = string.IsNullOrWhiteSpace(template) ? DefaultPattern : template;
        var s = Expand(pattern, c);
        if (!pattern.Contains("{ext}", StringComparison.OrdinalIgnoreCase)) s += "." + c.Ext;
        return Sanitize(s);
    }

    /// <summary>保存先フォルダ。日付フォルダは録音開始日で固定（c.Time）。</summary>
    public static string Folder(string baseFolder, bool dateFolder, string dateFormat, NameContext c)
    {
        if (!dateFolder) return baseFolder;
        var format = string.IsNullOrWhiteSpace(dateFormat) ? DefaultDateFormat : dateFormat;
        return Path.Combine(baseFolder, Sanitize(Expand(format, c)));
    }

    /// <summary>Windowsで使えない文字を置換する。コロンは全角「：」。</summary>
    public static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                ':' => '：', '\\' => '＼', '/' => '／', '*' => '＊', '?' => '？',
                '"' => '”', '<' => '＜', '>' => '＞', '|' => '｜',
                _ when ch < 32 => '_',
                _ => ch,
            });
        }
        var r = sb.ToString().Trim().TrimEnd('.', ' ');
        if (r.Length == 0) return "録音";
        var dot = r.IndexOf('.');
        if (Reserved.IsMatch(dot < 0 ? r : r[..dot])) r = "_" + r;
        return r;
    }

    /// <summary>{Device} 用。絵文字などの記号を除いたデバイス名。</summary>
    public static string CleanDevice(string label)
    {
        var sb = new StringBuilder();
        foreach (var rune in label.EnumerateRunes())
            if (rune.Value <= 0xFFFF) sb.Append(rune.ToString());
        return sb.ToString().Trim();
    }

    /// <summary>録音中に書く仮名。開始時刻・No・Track を含める（異常終了後の復旧用。Recovery が読む）。</summary>
    public static string TempName(DateTime start, int no, int track, string ext, int? lane = null) =>
        $"_rec_{start.ToString("yyyyMMdd_HHmmss", Inv)}_No{no}_T{track}" + (lane is { } l ? $"_D{l + 1}" : "") + $".{ext}";

    /// <summary>デバイスごとに別ファイルにするとき、パターンに {Device} が無ければ拡張子の手前に " {Device}" を足す。</summary>
    public static string WithDevice(string pattern)
    {
        if (pattern.Contains("{Device}", StringComparison.OrdinalIgnoreCase)) return pattern;
        const string tail = ".{ext}";
        if (pattern.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
            return pattern[..^tail.Length] + " {Device}" + pattern[^tail.Length..];
        return pattern + " {Device}";
    }

    /// <summary>同名があれば末尾に ` (2)` ` (3)` を付ける。パス全体が長すぎる場合は名前を短くする。</summary>
    public static string Unique(string folder, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var max = Math.Max(8, 250 - folder.Length - 1 - ext.Length - 6);
        if (stem.Length > max) stem = stem[..max].TrimEnd();
        var path = Path.Combine(folder, stem + ext);
        for (int n = 2; File.Exists(path) || Directory.Exists(path); n++)
            path = Path.Combine(folder, $"{stem} ({n}){ext}");
        return path;
    }
}
