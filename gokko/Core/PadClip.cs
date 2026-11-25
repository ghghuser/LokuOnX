namespace Gokko.Core;

/// <summary>読み込み済みのSE。48kHz・ステレオ・16bit のインターリーブ（L,R,L,R…）。</summary>
public sealed class PadClip
{
    public const int SampleRate = 48000;

    public short[] Pcm { get; }
    public int Frames => Pcm.Length / 2;
    public double Seconds => (double)Frames / SampleRate;

    public PadClip(short[] pcm) => Pcm = pcm;
}
