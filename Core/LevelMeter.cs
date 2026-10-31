namespace Recorder.Core;

public static class LevelMeter
{
    /// <summary>バッファ内の最大ピーク（0〜1。floatは1を超えることがある）を返す。</summary>
    /// <param name="format">f32le / s16le / s24le / s32le</param>
    public static float Peak(byte[] buf, int count, string format)
    {
        float peak = 0;
        switch (format)
        {
            case "f32le":
                for (int i = 0; i + 4 <= count; i += 4)
                {
                    var v = Math.Abs(BitConverter.ToSingle(buf, i));
                    if (v > peak) peak = v;
                }
                break;
            case "s16le":
                for (int i = 0; i + 2 <= count; i += 2)
                {
                    var v = Math.Abs((int)BitConverter.ToInt16(buf, i)) / 32768f;
                    if (v > peak) peak = v;
                }
                break;
            case "s24le":
                for (int i = 0; i + 3 <= count; i += 3)
                {
                    int s = buf[i] | (buf[i + 1] << 8) | ((sbyte)buf[i + 2] << 16);
                    var v = Math.Abs(s) / 8388608f;
                    if (v > peak) peak = v;
                }
                break;
            case "s32le":
                for (int i = 0; i + 4 <= count; i += 4)
                {
                    var v = Math.Abs((long)BitConverter.ToInt32(buf, i)) / 2147483648f;
                    if (v > peak) peak = v;
                }
                break;
        }
        return peak;
    }
}
