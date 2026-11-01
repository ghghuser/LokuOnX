namespace Recorder.Core;

/// <summary>
/// 一時停止・再開のつなぎ目のプツッという音を防ぐための短いフェード（約20ms）。
/// フェードアウトが終わるまでの音だけを通し、終わったら Apply が faded=true を返す。WPF や Windows API には依存しない。
/// </summary>
public sealed class Fader
{
    private readonly AudioFormatInfo _f;
    private readonly int _bytesPerSample;
    private readonly int _frameBytes;
    private readonly int _rampFrames;
    private readonly object _gate = new();
    private int _mode;     // 0=なし 1=フェードアウト 2=フェードイン
    private int _pos;      // ランプの何フレーム目か
    private bool _outDone;

    public Fader(AudioFormatInfo format, int milliseconds = 20)
    {
        _f = format;
        _bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        _frameBytes = _bytesPerSample * Math.Max(1, format.Channels);
        _rampFrames = Math.Max(1, format.SampleRate * milliseconds / 1000);
    }

    public void StartOut() { lock (_gate) { _mode = 1; _pos = 0; _outDone = false; } }
    public void StartIn() { lock (_gate) { _mode = 2; _pos = 0; _outDone = false; } }

    /// <summary>buf の先頭 count バイトにフェードを掛ける。返り値は使うバイト数。フェードアウトが終わったら faded=true。</summary>
    public int Apply(byte[] buf, int count, out bool faded)
    {
        faded = false;
        lock (_gate)
        {
            if (_mode == 0) return count;
            if (_mode == 1 && _outDone) return 0;
            int frames = count / _frameBytes;
            int used = 0;
            for (; used < frames; used++)
            {
                float gain = _mode == 1 ? 1f - (float)_pos / _rampFrames : (float)_pos / _rampFrames;
                Scale(buf, used * _frameBytes, gain);
                if (++_pos >= _rampFrames)
                {
                    used++;
                    if (_mode == 1) { _outDone = true; faded = true; }
                    else _mode = 0;
                    break;
                }
            }
            if (_mode == 0) return count;   // フェードイン完了。残りはそのまま
            return _outDone ? used * _frameBytes : count;
        }
    }

    private void Scale(byte[] b, int offset, float gain)
    {
        for (int c = 0; c < _f.Channels; c++)
        {
            int i = offset + c * _bytesPerSample;
            if (_f.IsFloat && _f.BitsPerSample == 32)
                BitConverter.TryWriteBytes(new Span<byte>(b, i, 4), BitConverter.ToSingle(b, i) * gain);
            else if (_f.BitsPerSample == 16)
                BitConverter.TryWriteBytes(new Span<byte>(b, i, 2), (short)(BitConverter.ToInt16(b, i) * gain));
            else if (_f.BitsPerSample == 24)
            {
                int s = (b[i + 2] << 24 | b[i + 1] << 16 | b[i] << 8) >> 8;
                s = (int)(s * gain);
                b[i] = (byte)s; b[i + 1] = (byte)(s >> 8); b[i + 2] = (byte)(s >> 16);
            }
            else if (_f.BitsPerSample == 32)
                BitConverter.TryWriteBytes(new Span<byte>(b, i, 4), (int)(BitConverter.ToInt32(b, i) * (double)gain));
        }
    }
}
