namespace Recorder.Core;

/// <summary>自動分割の間隔の表記（時:分:秒）と読み取り。WPF には依存しない。</summary>
public static class SplitTime
{
    public const int Max = 86400;   // 24時間
    public const int Min = 10;      // 0（無効）以外の最短

    public static string Format(int seconds)
    {
        seconds = Math.Clamp(seconds, 0, Max);
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    /// <summary>0 = 無効。1〜9秒は最短の10秒へ、24時間超は24時間へ。</summary>
    public static int Clamp(long seconds)
    {
        if (seconds <= 0) return 0;
        if (seconds < Min) return Min;
        return seconds > Max ? Max : (int)seconds;
    }

    /// <summary>
    /// 「1:30:00」「01:30」「90」のような入力を読む（全角の数字・コロンも可）。
    /// コロンが無い場合は右詰めで 時分秒 とみなす（130 → 00:01:30）。コロンが1つなら 分:秒。
    /// </summary>
    public static bool TryParse(string? text, out int seconds)
    {
        seconds = 0;
        var t = Digits.ToHalfWidth(text ?? "").Replace('：', ':').Trim();
        if (t.Length == 0) return false;
        var v = new long[3];
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length > 3) return false;
            int off = 3 - parts.Length;
            for (int i = 0; i < parts.Length; i++)
                if (!TryNum(parts[i].Length == 0 ? "0" : parts[i], out v[off + i])) return false;
        }
        else
        {
            if (t.Length > 6 || !TryNum(t, out _)) return false;
            t = t.PadLeft(6, '0');
            for (int i = 0; i < 3; i++) v[i] = long.Parse(t.Substring(i * 2, 2));
        }
        seconds = Clamp(v[0] * 3600 + v[1] * 60 + v[2]);
        return true;
    }

    private static bool TryNum(string s, out long n)
    {
        n = 0;
        foreach (var c in s) if (c < '0' || c > '9') return false;
        return s.Length > 0 && s.Length <= 9 && long.TryParse(s, out n);
    }
}
