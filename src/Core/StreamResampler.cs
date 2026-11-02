namespace Recorder.Core;

/// <summary>
/// 連続して届く float PCM（インターリーブ）を、ブロックの切れ目なしに変換する3次補間（Catmull-Rom）のリサンプラ。
/// step＝入力フレーム数÷出力フレーム数。サンプリング周波数の違いと、デバイスの時計ずれの補正（わずかな伸縮）の両方に使う。
/// WPF や Windows API には依存しない。
/// </summary>
public sealed class StreamResampler
{
    private readonly int _ch;
    private float[] _hist;
    private int _len;       // _hist に入っているフレーム数
    private double _pos;    // 次に出力する位置（_hist 内のフレーム位置）

    public StreamResampler(int channels)
    {
        _ch = Math.Max(1, channels);
        _hist = new float[_ch * 4096];
        _len = 1;           // 先頭の1フレームは無音（最初のサンプルの手前の補間用）
        _pos = 1.0;
    }

    /// <summary>input の先頭 frames フレームを足して、出せる分だけ変換して返す。返す配列は outFrames×チャンネル数 より長いことがある。</summary>
    public float[] Process(float[] input, int frames, double step, out int outFrames)
    {
        if (step <= 0.01) step = 0.01;
        int need = (_len + frames) * _ch;
        if (need > _hist.Length) Array.Resize(ref _hist, Math.Max(need, _hist.Length * 2));
        Array.Copy(input, 0, _hist, _len * _ch, frames * _ch);
        _len += frames;

        int cap = (int)Math.Max(0, (_len - 2 - _pos) / step) + 2;
        var output = new float[cap * _ch];
        int n = 0;
        while (_pos + 2 < _len && n < cap)
        {
            int i = (int)_pos;
            float t = (float)(_pos - i);
            for (int c = 0; c < _ch; c++)
            {
                float p0 = _hist[(i - 1) * _ch + c];
                float p1 = _hist[i * _ch + c];
                float p2 = _hist[(i + 1) * _ch + c];
                float p3 = _hist[(i + 2) * _ch + c];
                output[n * _ch + c] = p1 + 0.5f * t * (p2 - p0 + t * (2f * p0 - 5f * p1 + 4f * p2 - p3
                                                                   + t * (3f * (p1 - p2) + p3 - p0)));
            }
            n++;
            _pos += step;
        }

        // 使い終わった分を捨てる（補間に必要な直前の1フレームは残す）
        int drop = Math.Min((int)_pos - 1, _len - 1);
        if (drop > 0)
        {
            Array.Copy(_hist, drop * _ch, _hist, 0, (_len - drop) * _ch);
            _len -= drop;
            _pos -= drop;
        }
        outFrames = n;
        return output;
    }
}
