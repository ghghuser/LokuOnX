namespace Recorder.Core;

/// <summary>予約録音の「時刻（1日の中の何時何分）」の読み書きと、次にその時刻になる日時の計算。WPF には依存しない。</summary>
public static class ClockTime
{
    public const int MaxSecondOfDay = 86399;

    /// <summary>"07:30" の形（秒があるときだけ "07:30:15"）。</summary>
    public static string Format(int secondOfDay)
    {
        var s = Math.Clamp(secondOfDay, 0, MaxSecondOfDay);
        return s % 60 == 0
            ? $"{s / 3600:00}:{s / 60 % 60:00}"
            : $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
    }

    /// <summary>
    /// 「7:30」「07:30:15」「730」「0730」「19」（全角も可）を読む。
    /// コロンが無いときは 1〜2桁＝時、3〜4桁＝時分、5〜6桁＝時分秒。
    /// </summary>
    public static bool TryParse(string? text, out int secondOfDay)
    {
        secondOfDay = 0;
        var t = Digits.ToHalfWidth(text ?? "").Replace('：', ':').Trim();
        if (t.Length == 0) return false;
        long h, m = 0, s = 0;
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length is < 2 or > 3) return false;
            if (!TryNum(parts[0], out h) || !TryNum(parts[1], out m)) return false;
            if (parts.Length == 3 && !TryNum(parts[2], out s)) return false;
        }
        else
        {
            if (t.Length > 6 || !TryNum(t, out _)) return false;
            if (t.Length <= 2) h = long.Parse(t);
            else if (t.Length <= 4) { h = long.Parse(t[..^2]); m = long.Parse(t[^2..]); }
            else { h = long.Parse(t[..^4]); m = long.Parse(t[^4..^2]); s = long.Parse(t[^2..]); }
        }
        if (h > 23 || m > 59 || s > 59) return false;
        secondOfDay = (int)(h * 3600 + m * 60 + s);
        return true;
    }

    private static bool TryNum(string text, out long n)
    {
        n = 0;
        if (text.Length == 0 || text.Length > 6) return false;
        foreach (var c in text) if (c < '0' || c > '9') return false;
        return long.TryParse(text, out n);
    }

    /// <summary>now より後で、次にその時刻（1日の中の秒）になる日時。ちょうど now のときは翌日。</summary>
    public static DateTime Next(DateTime now, int secondOfDay)
    {
        var t = now.Date.AddSeconds(Math.Clamp(secondOfDay, 0, MaxSecondOfDay));
        return t > now ? t : t.AddDays(1);
    }
}
