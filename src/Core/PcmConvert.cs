using System.Runtime.InteropServices;

namespace Recorder.Core;

/// <summary>PCMバイト列 → float（-1〜1）への変換とチャンネル数の合わせ込み。WPF や Windows API には依存しない。</summary>
public static class PcmConvert
{
    /// <summary>この形式を変換できるか（16/24/32bit整数、32bit浮動小数）。</summary>
    public static bool Supports(AudioFormatInfo f) =>
        f.Channels >= 1 && (f.IsFloat ? f.BitsPerSample == 32 : f.BitsPerSample is 16 or 24 or 32);

    /// <summary>
    /// buf の先頭 count バイトを、outCh チャンネルの float（インターリーブ）にする。
    /// チャンネル数が違うときは、同数＝そのまま／モノへ＝平均／モノから＝全チャンネルへ複写／それ以外＝先頭から詰める（足りない分は0）。
    /// </summary>
    public static float[] ToFloat(byte[] buf, int count, AudioFormatInfo f, int outCh, out int frames)
    {
        int inCh = Math.Max(1, f.Channels);
        int bps = f.BitsPerSample / 8;
        int frameBytes = inCh * bps;
        frames = count / frameBytes;
        var result = new float[frames * outCh];
        if (inCh == outCh && frames > 0)
        {
            // よくある形式（float32／16bit整数・チャンネル数そのまま）は、1サンプルずつの分岐なしで一括変換する
            if (f.IsFloat && f.BitsPerSample == 32)
            {
                Buffer.BlockCopy(buf, 0, result, 0, frames * frameBytes);
                return result;
            }
            if (!f.IsFloat && f.BitsPerSample == 16)
            {
                var src = MemoryMarshal.Cast<byte, short>(buf.AsSpan(0, frames * frameBytes));
                for (int i = 0; i < src.Length; i++) result[i] = src[i] / 32768f;
                return result;
            }
        }
        var tmp = new float[inCh];
        for (int n = 0; n < frames; n++)
        {
            int o = n * frameBytes;
            for (int c = 0; c < inCh; c++) tmp[c] = Sample(buf, o + c * bps, f);
            Map(tmp, inCh, result, n * outCh, outCh);
        }
        return result;
    }

    private static float Sample(byte[] b, int i, AudioFormatInfo f)
    {
        if (f.IsFloat) return BitConverter.ToSingle(b, i);
        switch (f.BitsPerSample)
        {
            case 16: return BitConverter.ToInt16(b, i) / 32768f;
            case 24: return ((b[i + 2] << 24 | b[i + 1] << 16 | b[i] << 8) >> 8) / 8388608f;
            case 32: return BitConverter.ToInt32(b, i) / 2147483648f;
            default: return 0f;
        }
    }

    private static void Map(float[] src, int inCh, float[] dst, int o, int outCh)
    {
        if (inCh == outCh)
        {
            for (int c = 0; c < outCh; c++) dst[o + c] = src[c];
        }
        else if (outCh == 1)
        {
            float sum = 0;
            for (int c = 0; c < inCh; c++) sum += src[c];
            dst[o] = sum / inCh;
        }
        else if (inCh == 1)
        {
            for (int c = 0; c < outCh; c++) dst[o + c] = src[0];
        }
        else
        {
            for (int c = 0; c < outCh; c++) dst[o + c] = c < inCh ? src[c] : 0f;
        }
    }
}
