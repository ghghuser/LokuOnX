namespace Gokko.Core;

/// <summary>
/// 鳴っているパッドの音を足し合わせる（48kHz・ステレオ・float。±1に切り詰め）。UI のスレッドから Start/Stop、音の出力側のスレッドから Read を呼ぶ。
/// 同じパッドをもう一度鳴らすと、前の音は短く（約5ms）フェードアウトして止まる（プツッという音を防ぐ）。
/// </summary>
public sealed class PadMixer
{
    public const int FadeOutFrames = 240;
    public const int FadeInFrames = 96;

    private sealed class Voice
    {
        public int Id;
        public PadClip Clip = null!;
        public int Pos;
        public bool Loop;
        public float Gain;
        public int FadeInLeft = FadeInFrames;
        public bool Stopping;
        public int FadeOutLeft;
    }

    private readonly List<Voice> _voices = new();
    private readonly object _lock = new();
    private double _masterDb;

    /// <summary>全体の音量（dB）。左端（<see cref="PadConfig.MinDb"/>）はミュート。</summary>
    public double MasterDb
    {
        get => Volatile.Read(ref _masterDb);
        set => Volatile.Write(ref _masterDb, value);
    }

    /// <summary>鳴らす。同じ id が鳴っていれば、そちらはフェードアウトさせて新しく頭から。</summary>
    public void Start(int id, PadClip clip, bool loop, double volumeDb)
    {
        if (clip.Frames == 0) return;
        lock (_lock)
        {
            foreach (var v in _voices) if (v.Id == id) BeginFadeOut(v);
            _voices.Add(new Voice { Id = id, Clip = clip, Loop = loop, Gain = PadConfig.ToGain(volumeDb) });
        }
    }

    public void Stop(int id)
    {
        lock (_lock) foreach (var v in _voices) if (v.Id == id) BeginFadeOut(v);
    }

    public void StopAll()
    {
        lock (_lock) foreach (var v in _voices) BeginFadeOut(v);
    }

    /// <summary>フェードなしで、すべて即座に止める（「全停止」・設定の変更・異常時の最後の手段）。</summary>
    public void StopAllNow()
    {
        lock (_lock) _voices.Clear();
    }

    /// <summary>そのパッドの音だけ、フェードなしで即座に止める。</summary>
    public void StopNow(int id)
    {
        lock (_lock) _voices.RemoveAll(v => v.Id == id);
    }

    private static void BeginFadeOut(Voice v)
    {
        if (v.Stopping) return;
        v.Stopping = true;
        v.FadeOutLeft = FadeOutFrames;
    }

    public bool IsPlaying(int id)
    {
        lock (_lock) return _voices.Any(v => v.Id == id && !v.Stopping);
    }

    /// <summary>いま鳴っているパッドの進み具合（0〜1）。鳴っていなければ 0。</summary>
    public double Progress(int id)
    {
        lock (_lock)
        {
            var v = _voices.FirstOrDefault(x => x.Id == id && !x.Stopping);
            return v == null ? 0 : Math.Clamp((double)v.Pos / v.Clip.Frames, 0, 1);
        }
    }

    /// <summary>buffer の offset から count 個（L,R のインターリーブ）を埋める。無音でも必ず count 個ぶん返す。</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        var master = PadConfig.ToGain(MasterDb);
        var frames = count / 2;
        lock (_lock)
        {
            try
            {
            for (var vi = _voices.Count - 1; vi >= 0; vi--)
            {
                var v = _voices[vi];
                var pcm = v.Clip.Pcm;
                var done = false;
                for (var f = 0; f < frames; f++)
                {
                    if (v.Pos >= v.Clip.Frames)
                    {
                        if (v.Loop && !v.Stopping) v.Pos = 0;
                        else { done = true; break; }
                    }
                    var env = v.Gain * master;
                    if (v.FadeInLeft > 0) { env *= 1f - (float)v.FadeInLeft / FadeInFrames; v.FadeInLeft--; }
                    if (v.Stopping)
                    {
                        env *= (float)v.FadeOutLeft / FadeOutFrames;
                        v.FadeOutLeft--;
                    }
                    var o = offset + f * 2;
                    buffer[o] += pcm[v.Pos * 2] / 32768f * env;
                    buffer[o + 1] += pcm[v.Pos * 2 + 1] / 32768f * env;
                    v.Pos++;
                    if (v.Stopping && v.FadeOutLeft <= 0) { done = true; break; }
                }
                if (done) _voices.RemoveAt(vi);
            }
            }
            catch (Exception)
            {
                _voices.Clear();   // 音を出す側のスレッドで例外を出さない（出力が止まったままになるのを防ぐ）
                Array.Clear(buffer, offset, count);
            }
        }
        for (var i = 0; i < count; i++)
        {
            var s = buffer[offset + i];
            if (s > 1f) buffer[offset + i] = 1f;
            else if (s < -1f) buffer[offset + i] = -1f;
        }
        return count;
    }
}
