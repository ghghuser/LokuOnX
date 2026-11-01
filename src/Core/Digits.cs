namespace Recorder.Core;

/// <summary>全角数字（０〜９）を半角数字へ直す。WPF には依存しない。</summary>
public static class Digits
{
    public static string ToHalfWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] >= '０' && chars[i] <= '９') chars[i] = (char)('0' + (chars[i] - '０'));
        return new string(chars);
    }
}
