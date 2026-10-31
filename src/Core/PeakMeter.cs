namespace Recorder.Core;

/// <summary>PCMバイト列から最大ピーク（0.0〜1.0超）を求める。16/24/32bit整数と32bit浮動小数に対応。</summary>
public static class PeakMeter
{
    public static float Compute(byte[] buf, int count, AudioFormatInfo f)
    {
        float peak = 0;
        if (f.IsFloat && f.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= count; i += 4)
            {
                var v = Math.Abs(BitConverter.ToSingle(buf, i));
                if (v > peak) peak = v;
            }
        }
        else if (f.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= count; i += 2)
            {
                var v = Math.Abs(BitConverter.ToInt16(buf, i) / 32768f);
                if (v > peak) peak = v;
            }
        }
        else if (f.BitsPerSample == 24)
        {
            for (int i = 0; i + 3 <= count; i += 3)
            {
                int s = (buf[i + 2] << 24 | buf[i + 1] << 16 | buf[i] << 8) >> 8;
                var v = Math.Abs(s / 8388608f);
                if (v > peak) peak = v;
            }
        }
        else if (f.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= count; i += 4)
            {
                var v = Math.Abs(BitConverter.ToInt32(buf, i) / 2147483648f);
                if (v > peak) peak = v;
            }
        }
        return peak;
    }
}
