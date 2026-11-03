using System.Diagnostics;
using System.Text;
using NAudio.Wave;

namespace Recorder.Core.Win;

/// <summary>
/// 再生（Windows 専用）。FFmpeg でファイルを読んで（速度は atempo。音程は変わらない）PCMを標準出力へ流し、
/// NAudio の WaveOutEvent で鳴らす。どの形式（flac/mp3/m4a/aac/wav/ogg/opus）も同じ経路。
/// シーク・速度変更は、新しい位置・速度で FFmpeg を起こし直す。UIスレッドで作って使う。
/// </summary>
public sealed class FfmpegPlayer : IDisposable
{
    private static readonly WaveFormat Format = new(PlaybackArgs.SampleRate, 16, PlaybackArgs.Channels);
    private readonly string _ffmpeg;
    private Process? _proc;
    private WaveOutEvent? _out;
    private double _start;
    private double _speed = 1.0;
    private int _gen;

    public FfmpegPlayer(string ffmpegPath) => _ffmpeg = ffmpegPath;

    public bool IsPlaying => _out != null;

    /// <summary>元のファイル上の現在位置（秒）。止まっているときは最後に止めた位置。</summary>
    public double Position =>
        _out == null ? _start : _start + _out.GetPosition() / (double)Format.AverageBytesPerSecond * _speed;

    /// <summary>最後まで再生して終わった。</summary>
    public event Action? Ended;

    /// <summary>再生できなかった（FFmpegが読めなかった等）。理由つき。</summary>
    public event Action<string>? Failed;

    public void Play(string path, double startSeconds, double speed)
    {
        Halt();
        var gen = ++_gen;
        _start = Math.Max(0, startSeconds);
        _speed = PlaybackArgs.ClampSpeed(speed);

        var psi = new ProcessStartInfo(_ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in PlaybackArgs.Build(path, _start, _speed)) psi.ArgumentList.Add(a);

        Process? proc = null;
        try
        {
            proc = Process.Start(psi) ?? throw new IOException("FFmpegを起動できませんでした。");
            var err = new StringBuilder();
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (err) { if (err.Length < 600) err.AppendLine(e.Data); }
            };
            proc.BeginErrorReadLine();

            var source = new PipeProvider(proc.StandardOutput.BaseStream, Format);
            var output = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
            output.PlaybackStopped += (_, e) => OnStopped(gen, source, err, e.Exception);
            output.Init(source);
            _proc = proc;
            _out = output;
            output.Play();
        }
        catch (Exception ex)
        {
            Halt();
            try { if (proc is { HasExited: false }) proc.Kill(true); } catch (Exception) { }
            Failed?.Invoke(ex.Message);
        }
    }

    /// <summary>止める。位置は覚えておく（次の Play で続きから始められる）。</summary>
    public void Stop()
    {
        if (_out != null) _start = Math.Max(0, Position);
        Halt();
    }

    private void OnStopped(int gen, PipeProvider source, StringBuilder err, Exception? error)
    {
        if (gen != _gen) return;   // 自分で止めた・次の再生に切り替えた
        string detail;
        lock (err) detail = err.ToString().Trim();
        _start = 0;
        Halt();
        if (error != null) Failed?.Invoke("再生できませんでした: " + error.Message);
        else if (source.BytesRead == 0 && detail.Length > 0) Failed?.Invoke("再生できませんでした: " + detail);
        else Ended?.Invoke();
    }

    /// <summary>FFmpeg を先に止めて（標準出力が閉じ、再生スレッドが抜ける）から、出力を止める。</summary>
    private void Halt()
    {
        _gen++;
        var proc = _proc;
        var output = _out;
        _proc = null;
        _out = null;
        try
        {
            if (proc is { HasExited: false })
            {
                proc.Kill(true);
                proc.WaitForExit(500);   // ファイルを手放すまで少し待つ（直後に名前の変更・削除をするため）
            }
        }
        catch (Exception) { }
        try { output?.Stop(); } catch (Exception) { }
        try { output?.Dispose(); } catch (Exception) { }
        try { proc?.Dispose(); } catch (Exception) { }
    }

    public void Dispose() => Halt();

    /// <summary>FFmpeg の標準出力（PCM）をそのまま NAudio へ渡す。読んだ量も数える。</summary>
    private sealed class PipeProvider : IWaveProvider
    {
        private readonly Stream _stream;
        public PipeProvider(Stream stream, WaveFormat format)
        {
            _stream = stream;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }
        public long BytesRead { get; private set; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            try
            {
                while (total < count)
                {
                    var n = _stream.Read(buffer, offset + total, count - total);
                    if (n <= 0) break;
                    total += n;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
            BytesRead += total;
            return total;
        }
    }
}
