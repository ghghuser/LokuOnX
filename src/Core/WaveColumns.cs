namespace Recorder.Core;

/// <summary>
/// 全体波形用のデータ。録音する音（L+Rを平均したモノラル）を 0.04 秒ごとに区切り、各区間の最小値・最大値（-1.0〜+1.0）を貯める。
/// 音声スレッドで Feed、画面（UIスレッド）で Take して取り出す。取り出さないまま溜まっても、古いものから捨てる（上限 3000 区間）。
/// </summary>
public sealed class WaveColumns
{
    public const double ColumnSeconds = 0.04;
    private const int MaxPending = 3000;

    private readonly object _lock = new();
    private readonly List<(float Min, float Max)> _done = new();
    private double _acc;
    private float _min = float.MaxValue, _max = float.MinValue;

    /// <summary>PCM（16/24/32bit整数・32bit浮動小数）を渡す。</summary>
    public void Feed(byte[] buf, int count, AudioFormatInfo f)
    {
        int bytes = f.BitsPerSample / 8, ch = Math.Max(1, f.Channels);
        if (bytes <= 0 || f.SampleRate <= 0) return;
        int frameBytes = bytes * ch, frames = count / frameBytes;
        double perColumn = f.SampleRate * ColumnSeconds;
        lock (_lock)
        {
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                int o = i * frameBytes;
                for (int c = 0; c < ch; c++) sum += Sample(buf, o + c * bytes, f);
                float m = sum / ch;
                if (m < _min) _min = m;
                if (m > _max) _max = m;
                if (++_acc >= perColumn)
                {
                    _done.Add((_min, _max));
                    _acc -= perColumn;
                    _min = float.MaxValue;
                    _max = float.MinValue;
                }
            }
            if (_done.Count > MaxPending) _done.RemoveRange(0, _done.Count - MaxPending);
        }
    }

    private static float Sample(byte[] b, int o, AudioFormatInfo f)
    {
        if (f.IsFloat && f.BitsPerSample == 32) return BitConverter.ToSingle(b, o);
        return f.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(b, o) / 32768f,
            24 => ((b[o + 2] << 24 | b[o + 1] << 16 | b[o] << 8) >> 8) / 8388608f,
            32 => BitConverter.ToInt32(b, o) / 2147483648f,
            _ => 0f,
        };
    }

    /// <summary>前回取得以降にできた区間（古い順）を返して空にする。</summary>
    public (float Min, float Max)[] Take()
    {
        lock (_lock)
        {
            var copy = _done.ToArray();
            _done.Clear();
            return copy;
        }
    }
}
