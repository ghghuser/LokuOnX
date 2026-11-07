namespace Recorder.Core;

/// <summary>
/// 1台ぶんの入力を、ミックス用の共通形式（float・サンプリング周波数・チャンネル数）の「時間軸」に並べる。
/// ・データが来ない間（ループバックの無音・デバイス切断中）は壁時計に合わせて無音で埋める。
/// ・デバイスの時計が壁時計より進む／遅れるときは、±0.1%のわずかな伸縮で少しずつ合わせる（PROJECT.md §5-3）。
/// ・ゲイン（音量調整）は滑らかに変化させる。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class DeviceStream
{
    private const double HardLagSec = 0.08;     // これ以上遅れたら（無音区間の後など）無音で埋めて壁時計に合わせ直す
    private const double HardAheadSec = 0.5;    // これ以上進みすぎたら（時計が大きく速いデバイス）、届いたブロックを捨てて壁時計に合わせ直す
    private const double SlewStartSec = 0.04;   // 遅れ・進みがこれを超えたら伸縮を始める
    private const double SlewStopSec = 0.01;    // これ以内に戻ったら止める
    private const double SlewRate = 0.001;      // 伸縮の割合（0.1%。約1.7セント＝聞こえない）
    private const double LagAlpha = 0.02;       // 遅れの平滑化

    private readonly object _lock = new();
    private readonly int _rate;
    private readonly int _ch;
    private readonly Func<double> _now;          // 壁時計（秒）
    private readonly FloatRing _ring = new();
    private AudioFormatInfo? _fmt;
    private StreamResampler? _rs;
    private long _written;                       // 時間軸に並べたフレーム数（無音の補填を含む）
    private double _lagEma;
    private int _slew;                           // +1 = 少し伸ばす / -1 = 少し縮める / 0 = そのまま
    private float _gain = 1f;
    private float _gainTarget = 1f;
    private float _peak;
    private bool _closed;

    public DeviceStream(int rate, int channels, Func<double> now, double gainDb)
    {
        _rate = Math.Max(1, rate);
        _ch = Math.Max(1, channels);
        _now = now;
        _gain = _gainTarget = DbToGain(gainDb);
    }

    private static float DbToGain(double db) => GainMath.DbToLinear(db);

    /// <summary>ゲイン（dB）。録音中も変えられる（滑らかに切り替わる）。</summary>
    public void SetGainDb(double db)
    {
        lock (_lock) _gainTarget = DbToGain(db);
    }

    /// <summary>入力デバイスがつながった（つなぎ直した）とき。時間軸は続けたまま、変換だけ作り直す。</summary>
    public void SetFormat(AudioFormatInfo fmt)
    {
        if (!PcmConvert.Supports(fmt))
            throw new NotSupportedException($"対応していない音声形式です（{fmt.BitsPerSample}bit {(fmt.IsFloat ? "float" : "整数")}）");
        lock (_lock)
        {
            _fmt = fmt;
            _rs = null;
            _lagEma = 0;
            _slew = 0;
        }
    }

    /// <summary>入力が切れたとき。以後は無音で埋まる。</summary>
    public void ClearFormat()
    {
        lock (_lock) _fmt = null;
    }

    public void Close()
    {
        lock (_lock) _closed = true;
    }

    /// <summary>ゲインを掛けたあとの、前回取得以降のピーク（0.0〜）を返してリセットする。</summary>
    public float TakePeak()
    {
        lock (_lock)
        {
            var p = _peak;
            _peak = 0;
            return p;
        }
    }

    /// <summary>デバイスから届いたPCM（録音スレッドから）。</summary>
    public void Push(byte[] buf, int count)
    {
        lock (_lock)
        {
            var fmt = _fmt;
            if (_closed || fmt == null) return;
            var f = PcmConvert.ToFloat(buf, count, fmt, _ch, out int frames);
            if (frames <= 0) return;
            ApplyGainAndPeak(f, frames);

            // 伸縮が必要なとき、またはサンプリング周波数が違うときだけ変換する
            float[] r;
            int n;
            if (fmt.SampleRate == _rate && _slew == 0)
            {
                r = f;
                n = frames;
                _rs = null;
            }
            else
            {
                _rs ??= new StreamResampler(_ch);
                double ratio = 1.0 + _slew * SlewRate;   // 出力フレーム数 ÷ 入力フレーム数
                r = _rs.Process(f, frames, (double)fmt.SampleRate / _rate / ratio, out n);
            }
            if (n <= 0) return;

            // このデータが終わった時刻は「いま」。壁時計に対して、時間軸がどれだけ遅れているか
            double lag = _now() - (double)n / _rate - (double)_written / _rate;
            if (lag < -HardAheadSec) return;
            if (lag > HardLagSec)
            {
                AppendSilence((long)(lag * _rate));
                _lagEma = 0;
                _slew = 0;
            }
            else
            {
                UpdateSlew(lag);
            }
            _ring.Append(r, n * _ch);
            _written += n;
        }
    }

    private void UpdateSlew(double lag)
    {
        _lagEma += LagAlpha * (lag - _lagEma);
        if (_slew == 0)
        {
            if (_lagEma > SlewStartSec) _slew = 1;
            else if (_lagEma < -SlewStartSec) _slew = -1;
        }
        else if (_slew > 0 && _lagEma < SlewStopSec) _slew = 0;
        else if (_slew < 0 && _lagEma > -SlewStopSec) _slew = 0;
    }

    private void ApplyGainAndPeak(float[] f, int frames)
    {
        float g0 = _gain, g1 = _gainTarget;
        _gain = g1;
        float peak = _peak;
        if (g0 == g1)
        {
            // ゲインが変化中でないとき（ほとんどの間）は、フレームごとの補間計算を省く
            int total = frames * _ch;
            for (int i = 0; i < total; i++)
            {
                float v = f[i] * g1;
                f[i] = v;
                float a = Math.Abs(v);
                if (a > peak) peak = a;
            }
            _peak = peak;
            return;
        }
        for (int n = 0; n < frames; n++)
        {
            float g = g0 == g1 ? g1 : g0 + (g1 - g0) * n / frames;
            for (int c = 0; c < _ch; c++)
            {
                int i = n * _ch + c;
                float v = f[i] * g;
                f[i] = v;
                float a = Math.Abs(v);
                if (a > peak) peak = a;
            }
        }
        _peak = peak;
    }

    private void AppendSilence(long frames)
    {
        if (frames <= 0) return;
        _ring.AppendZeros(frames * _ch);
        _written += frames;
    }

    /// <summary>ミックス用に、先頭から frames フレームを mix へ足し込む。足りない分は無音で埋めて時間軸を進める。scratch は frames×チャンネル数 以上の作業用配列。</summary>
    public void MixInto(float[] mix, int frames, float[] scratch)
    {
        lock (_lock)
        {
            long have = _ring.Count / _ch;
            if (have < frames) AppendSilence(frames - have);
            int floats = frames * _ch;
            int got = _ring.Take(scratch, floats);
            for (int i = 0; i < got; i++) mix[i] += scratch[i];
        }
    }
}
