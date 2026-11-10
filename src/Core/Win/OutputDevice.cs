using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Recorder.Core.Win;

/// <summary>
/// 再生デバイス1台への出力（WASAPI共有モード・イベント駆動。M11-2。LokuOn の OutputDevice を移したもの）。Windows専用。
/// 音づくりは Core の OutputRenderer が行い、ここはデバイスを開いてコールバックから Render を呼ぶだけ。録音には触れない（失敗しても例外は外に出さない）。
/// </summary>
public sealed class OutputDevice : IOutputDevice
{
    /// <summary>
    /// デバイスが答えた形式（MixFormat）そのままの形で音を渡す。Bluetoothなどは、形式が少しでも違うと
    /// 「Value does not fall within the expected range」で開けないことがあるため、float 以外（16/24/32bit整数）にも対応する。
    /// </summary>
    private sealed class RenderProvider : IWaveProvider
    {
        private readonly OutputRenderer r;
        private readonly int ch, bits;
        private readonly bool isFloat;
        private float[] scratch = Array.Empty<float>();

        public RenderProvider(OutputRenderer r, WaveFormat fmt)
        {
            this.r = r;
            ch = fmt.Channels;
            WaveFormat = fmt;
            bits = fmt.BitsPerSample;
            isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat
                || (fmt is WaveFormatExtensible ex && ex.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        }

        public WaveFormat WaveFormat { get; }

        public static bool Supports(WaveFormat f)
            => f.Encoding == WaveFormatEncoding.IeeeFloat || f.Encoding == WaveFormatEncoding.Pcm || f is WaveFormatExtensible;

        public int Read(byte[] buffer, int offset, int count)
        {
            int bytesPer = bits / 8, frameBytes = bytesPer * ch;
            int frames = count / frameBytes;
            int samples = frames * ch;
            if (scratch.Length < samples) scratch = new float[samples];   // 作業用の配列（Render のたびに作らない）
            r.Render(scratch, 0, frames);
            if (isFloat && bits == 32)
            {
                Buffer.BlockCopy(scratch, 0, buffer, offset, samples * 4);
            }
            else
            {
                int o = offset;
                for (int i = 0; i < samples; i++)
                {
                    float v = scratch[i];
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    switch (bits)
                    {
                        case 16: { short x = (short)Math.Round(v * 32767f); buffer[o++] = (byte)x; buffer[o++] = (byte)(x >> 8); break; }
                        case 24: { int x = (int)Math.Round(v * 8388607f); buffer[o++] = (byte)x; buffer[o++] = (byte)(x >> 8); buffer[o++] = (byte)(x >> 16); break; }
                        default: { int x = (int)Math.Round(v * 2147483647.0); buffer[o++] = (byte)x; buffer[o++] = (byte)(x >> 8); buffer[o++] = (byte)(x >> 16); buffer[o++] = (byte)(x >> 24); break; }
                    }
                }
            }
            int used = frames * frameBytes;
            for (int i = used; i < count; i++) buffer[offset + i] = 0;
            return count;
        }
    }

    private readonly string _id;
    private WasapiOut? _wo;
    private MMDevice? _dev;

    public OutputRenderer? Renderer { get; private set; }
    private volatile bool _failed;
    public bool Failed => _failed;
    public string LastError { get; private set; } = "";

    public OutputDevice(string id) => _id = id;

    /// <summary>
    /// デバイスを開いて再生を始める。失敗したら理由を返す（成功なら null）。
    /// Bluetoothなどは短いバッファやイベント駆動を受け付けないことがあるので、設定を変えながら最大5通り試す。
    /// </summary>
    public string? Start(Func<int, int, OutputRenderer> factory)
    {
        // (イベント駆動, バッファms)
        var attempts = new (bool Event, int Ms)[] { (true, 60), (true, 120), (true, 250), (false, 120), (false, 300) };
        string last = "", fmtText = "不明";
        foreach (var (ev, ms) in attempts)
        {
            MMDevice? d = null;
            WasapiOut? w = null;
            try
            {
                using var en = new MMDeviceEnumerator();
                d = _id == OutputDeviceInfo.DefaultId || _id.Length == 0
                    ? en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
                    : en.GetDevice(_id);
                var mix = d.AudioClient.MixFormat;
                fmtText = $"{mix.SampleRate}Hz {mix.Channels}ch {mix.BitsPerSample}bit {mix.Encoding}";
                var fmt = RenderProvider.Supports(mix) && (mix.BitsPerSample is 16 or 24 or 32)
                    ? mix : WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
                var r = factory(fmt.SampleRate, fmt.Channels);
                w = new WasapiOut(d, AudioClientShareMode.Shared, ev, ms);
                w.PlaybackStopped += (s, e) =>
                {
                    if (e.Exception != null) { LastError = e.Exception.Message; _failed = true; }
                };
                w.Init(new RenderProvider(r, fmt));
                w.Play();
                Renderer = r;
                _wo = w;
                _dev = d;
                _failed = false;
                LastError = "";
                return null;
            }
            catch (Exception ex)
            {
                last = $"{ex.Message} (0x{ex.HResult:X8} / デバイスの形式: {fmtText} / {(ev ? "イベント駆動" : "ポーリング")}・バッファ{ms}ms)";
                try { w?.Dispose(); } catch (Exception) { }
                try { d?.Dispose(); } catch (Exception) { }
            }
        }
        LastError = last;
        _failed = true;
        return $"出力デバイスを開けません: {last}";
    }

    public void Dispose()
    {
        try { _wo?.Stop(); } catch (Exception) { }
        try { _wo?.Dispose(); } catch (Exception) { }
        try { _dev?.Dispose(); } catch (Exception) { }
        _wo = null;
        _dev = null;
    }
}
