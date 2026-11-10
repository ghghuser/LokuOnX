namespace Recorder.Core;

/// <summary>
/// 1台ぶんの入力を、ミックス用の共通形式（float・サンプリング周波数・チャンネル数）の「時間軸」に並べる。
/// ・データが来ない間（ループバックの無音・デバイス切断中）は壁時計に合わせて無音で埋める。
/// ・デバイスの時計が壁時計より進む／遅れるときは、±0.1%のわずかな伸縮で少しずつ合わせる（PROJECT.md §5-3）。
/// ・ゲイン（音量調整）は滑らかに変化させる。
/// ・M11-2: 入力デバイスは録音の開始・終了で開き直さない（InputHub が持ち主）。録音につながっていない間（Attach 前・Detach 後）は、
///   ゲイン・ピーク・出力用のタップだけを処理し、時間軸（ring）には溜めない。録音に Attach すると、ringを空にして壁時計に合わせ直す。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed partial class DeviceStream
{
    private const double HardLagSec = 0.08;     // これ以上遅れたら（無音区間の後など）無音で埋めて壁時計に合わせ直す
    private const double HardAheadSec = 0.5;    // これ以上進みすぎたら（時計が大きく速いデバイス）、届いたブロックを捨てて壁時計に合わせ直す
    private const double SlewStartSec = 0.04;   // 遅れ・進みがこれを超えたら伸縮を始める
    private const double SlewStopSec = 0.01;    // これ以内に戻ったら止める
    private const double SlewRate = 0.001;      // 伸縮の割合（0.1%。約1.7セント＝聞こえない）
    private const double LagAlpha = 0.02;       // 遅れの平滑化

    private readonly object _lock = new();
    private int _rate;
    private int _ch;
    private readonly Func<double> _now;          // 壁時計（秒。InputHub の共通の時計）
    private bool _attached;                      // 録音（MixedSource）につながっているか
    private InputTap[] _taps = Array.Empty<InputTap>();   // 出力用の複製先（入力ゲインを掛けたあとの音）
    private int _tapCh = 2;                      // 複製先のチャンネル数（デバイスのチャンネル数、最大2。録音の開始・終了で変えない＝出力が途切れない）
    private float[] _tapScratch = Array.Empty<float>();
    private readonly FloatRing _ring = new();
    private AudioFormatInfo? _fmt;
    private StreamResampler? _rs;
    private long _written;                       // 時間軸に並べたフレーム数（無音の補填を含む）
    private double _lagEma;
    private int _slew;                           // +1 = 少し伸ばす / -1 = 少し縮める / 0 = そのまま
    private float _gain = 1f;
    private float _gainTarget = 1f;
    private float _peak;
    private float _peakL, _peakR;               // 左右別のピーク（ゲイン後。個別メーターのL/R用）
    private bool _closed;

    public DeviceStream(int rate, int channels, Func<double> now, double gainDb)
    {
        _rate = Math.Max(1, rate);
        _ch = Math.Max(1, channels);
        _now = now;
        _gain = _gainTarget = DbToGain(gainDb);
    }

    private static float DbToGain(double db) => GainMath.DbToLinear(db);

    /// <summary>出力用の複製先を足す。複製先は、届いた音（入力ゲイン後・デバイス本来のサンプリング周波数・いまのチャンネル数）を受け取る。</summary>
    public void AddTap(InputTap tap)
    {
        lock (_lock)
        {
            if (Array.IndexOf(_taps, tap) >= 0) return;
            _taps = _taps.Append(tap).ToArray();
        }
    }

    public void RemoveTap(InputTap tap)
    {
        lock (_lock)
        {
            _taps = _taps.Where(t => !ReferenceEquals(t, tap)).ToArray();
            tap.Buffer = null;
        }
    }

    /// <summary>録音につなぐ。共通のサンプリング周波数・チャンネル数に合わせ、時間軸を startSec（InputHub の時計）から始める。ringは空にする（録音の前にたまった音を混ぜない）。</summary>
    public void Attach(int rate, int channels, double startSec)
    {
        lock (_lock)
        {
            _rate = Math.Max(1, rate);
            _ch = Math.Max(1, channels);
            _ring.Clear();
            _written = (long)(startSec * _rate);
            _lagEma = 0;
            _slew = 0;
            _rs = null;
            _peak = _peakL = _peakR = 0;    // 録音の前にたまったピークは、録音のメーターに出さない
            _attached = true;
        }
    }

    /// <summary>録音から外す（デバイスは開いたまま）。</summary>
    public void Detach()
    {
        lock (_lock)
        {
            _attached = false;
            _ring.Clear();
            _rs = null;
            _ch = _tapCh;
        }
    }

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
            _tapCh = Math.Max(1, Math.Min(2, fmt.Channels));
            if (!_attached) _ch = _tapCh;   // 録音につながっていない間は、出力用に最大ステレオ
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

    /// <summary>ゲインを掛けたあとの、前回取得以降の左右のピークを返してリセットする（モノは左右同じ）。</summary>
    public (float L, float R) TakePeakLR()
    {
        lock (_lock)
        {
            var l = _peakL;
            var r = _ch > 1 ? _peakR : _peakL;
            _peakL = _peakR = 0;
            return (l, r);
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
            ApplyGainAndPeak(f, frames);   // ピーク（メーター・無音検出・自動開始）はここ＝エフェクトの前（Dry）で測る
            var (recSrc, tapSrc) = ApplyEffect(f, frames, fmt.SampleRate);   // M11-4: 録音・出力へ反映するときだけエフェクト後の音
            PushTaps(tapSrc, frames, fmt.SampleRate);
            if (!_attached) return;

            // 伸縮が必要なとき、またはサンプリング周波数が違うときだけ変換する
            float[] r;
            int n;
            if (fmt.SampleRate == _rate && _slew == 0)
            {
                r = recSrc;
                n = frames;
                _rs = null;
            }
            else
            {
                _rs ??= new StreamResampler(_ch);
                double ratio = 1.0 + _slew * SlewRate;   // 出力フレーム数 ÷ 入力フレーム数
                r = _rs.Process(recSrc, frames, (double)fmt.SampleRate / _rate / ratio, out n);
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

    /// <summary>出力用の複製先へ、ゲインを掛けたあとの音を渡す（複製先のバッファは、形式が変わっていたら作り直す）。
    /// チャンネル数は録音の形式（_ch）ではなくデバイス本来（最大2）に固定し、録音の開始・終了で複製先を作り直さない。</summary>
    private void PushTaps(float[] f, int frames, int rate)
    {
        var taps = _taps;
        if (taps.Length == 0) return;
        var src = _ch == _tapCh ? f : ToTapChannels(f, frames);
        for (int i = 0; i < taps.Length; i++)
        {
            var b = taps[i].Buffer;
            if (b == null || b.SampleRate != rate || b.Channels != _tapCh)
            {
                b = new SourceBuffer(rate, _tapCh);
                taps[i].Buffer = b;
            }
            b.Push(src, 0, frames);
        }
    }

    /// <summary>録音用のチャンネル数（_ch）の音を、複製先のチャンネル数（_tapCh）へ。モノ→左右に複製、ステレオ→モノは平均、3ch以上は先頭の2ch。</summary>
    private float[] ToTapChannels(float[] f, int frames)
    {
        int n = frames * _tapCh;
        if (_tapScratch.Length < n) _tapScratch = new float[n];
        int sc = _ch, dc = _tapCh;
        for (int i = 0; i < frames; i++)
        {
            int s = i * sc, d = i * dc;
            if (dc == 1) _tapScratch[d] = sc > 1 ? (f[s] + f[s + 1]) * 0.5f : f[s];
            else
            {
                _tapScratch[d] = f[s];
                _tapScratch[d + 1] = sc > 1 ? f[s + 1] : f[s];
            }
        }
        return _tapScratch;
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
            TrackChannels(f, frames);
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
        TrackChannels(f, frames);
    }

    /// <summary>左右別のピーク（ゲインを掛けたあとの f から）。</summary>
    private void TrackChannels(float[] f, int frames)
    {
        float l = _peakL, r = _peakR;
        for (int n = 0; n < frames; n++)
        {
            int i = n * _ch;
            float a = Math.Abs(f[i]);
            if (a > l) l = a;
            if (_ch > 1)
            {
                float b = Math.Abs(f[i + 1]);
                if (b > r) r = b;
            }
        }
        _peakL = l;
        _peakR = r;
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
