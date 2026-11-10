namespace Recorder.Core;

/// <summary>
/// DeviceStream のうち、入力ごとのエフェクト（M11-4。PROJECT.md §3-9）。
/// エフェクトは「ゲインとピークの測定の直後」、つまりデバイス本来のサンプリング周波数のまま掛ける（周波数変換・時間軸への並べ替えより前）。
/// ・ピーク（メーター・無音検出・自動録音開始）は掛ける前の音（Dry）で測る。
/// ・「録音に反映」がオンのときだけ、録音（時間軸）へ入る音がエフェクト後になる。既定はオフ＝録音は素の音。
/// ・「出力に反映」がオンのとき（既定）、出力用の複製先（タップ）へ届く音がエフェクト後になる。
/// ・どちらも掛けない設定・バイパス・何にも使われていない間は、配列に一切触れない（従来どおり）。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed partial class DeviceStream
{
    private EffectSetting _fxSet = new();
    private EffectChain? _fx;
    private int _fxRate, _fxCh;

    /// <summary>この入力のエフェクト設定を渡す（コピーして持つ。次に届く音から反映。録音中・出力中でも変えられる）。</summary>
    public void SetEffect(EffectSetting setting)
    {
        var c = setting.Clone();
        c.Normalize();
        lock (_lock)
        {
            _fxSet = c;
            _fx?.Update(c);
        }
    }

    /// <summary>
    /// f（ゲイン後・デバイス本来の周波数・_ch チャンネル）にエフェクトを掛け、(録音へ入れる音, 出力用の複製先へ入れる音) を返す。
    /// 両方へ反映するときは f をその場で書き換えて同じ配列を返す。片方だけのときは、反映する側にだけ新しい配列（コピーに掛けたもの）を返す。
    /// 返した配列は、録音側の FloatRing がそのまま持つことがあるので、使い回さない。_lock の中から呼ぶ。
    /// </summary>
    private (float[] Rec, float[] Tap) ApplyEffect(float[] f, int frames, int rate)
    {
        var s = _fxSet;
        bool rec = s.ToRecording && _attached;
        bool outp = s.ToOutput && _taps.Length > 0;
        if (!s.IsActive || (!rec && !outp)) return (f, f);
        if (_fx == null || _fxRate != rate || _fxCh != _ch)
        {
            _fx = new EffectChain(rate, _ch);
            _fx.Update(s);
            _fxRate = rate;
            _fxCh = _ch;
        }
        if (rec && outp)
        {
            _fx.Process(f, frames);
            return (f, f);
        }
        var wet = new float[frames * _ch];
        Array.Copy(f, wet, wet.Length);
        _fx.Process(wet, frames);
        return rec ? (wet, f) : (f, wet);
    }
}
