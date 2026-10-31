using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Recorder.Core;

/// <summary>
/// FFmpeg を子プロセスとして起動し、標準入力にPCMを流して flac / mp3 へ直接書き出す。
/// 書き込みは専用スレッド＋キューで行い、FFmpegが詰まっても録音スレッドを止めない（PROJECT.md §5-5）。
/// </summary>
public sealed class FfmpegEncoder : IDisposable
{
    private readonly string _ffmpegPath;
    private readonly string _outputPath;
    private readonly string _format;
    private readonly AudioFormatInfo _fmt;
    private readonly BlockingCollection<byte[]> _queue = new(4096);
    private readonly StringBuilder _stderr = new();
    private Process? _proc;
    private Thread? _writer;
    private volatile string? _failure;

    /// <summary>失敗の理由（正常なら null）。</summary>
    public string? Failure => _failure;

    /// <summary>失敗したとき一度だけ呼ばれる（別スレッドから）。</summary>
    public event Action<string>? Failed;

    public FfmpegEncoder(string ffmpegPath, string outputPath, string format, AudioFormatInfo fmt)
    {
        _ffmpegPath = ffmpegPath;
        _outputPath = outputPath;
        _format = format;
        _fmt = fmt;
    }

    private static string InputFormatName(AudioFormatInfo f)
    {
        if (f.IsFloat) return f.BitsPerSample == 64 ? "f64le" : "f32le";
        return f.BitsPerSample switch { 16 => "s16le", 24 => "s24le", 32 => "s32le", _ => "s16le" };
    }

    public void Start()
    {
        var psi = new ProcessStartInfo(_ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y",
                     "-f", InputFormatName(_fmt), "-ar", _fmt.SampleRate.ToString(),
                     "-ac", _fmt.Channels.ToString(), "-i", "pipe:0", "-vn" })
            psi.ArgumentList.Add(a);

        if (_format == "mp3")
        {
            foreach (var a in new[] { "-c:a", "libmp3lame", "-b:a", "192k", "-id3v2_version", "3" })
                psi.ArgumentList.Add(a);
        }
        else
        {
            foreach (var a in new[] { "-c:a", "flac" }) psi.ArgumentList.Add(a);
        }
        psi.ArgumentList.Add(_outputPath);

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_stderr)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > 4000) _stderr.Remove(0, _stderr.Length - 4000);
            }
        };
        _proc.Start();
        _proc.BeginErrorReadLine();

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "FfmpegWriter" };
        _writer.Start();
    }

    /// <summary>PCMを渡す。キューが満杯なら少し待ち、それでも駄目なら失敗扱いにする。</summary>
    public void Write(byte[] data)
    {
        if (_failure != null) return;
        if (!_queue.TryAdd(data, 2000)) Fail("エンコードが追いつきません（FFmpegが応答しません）");
    }

    private void WriteLoop()
    {
        try
        {
            var stdin = _proc!.StandardInput.BaseStream;
            foreach (var data in _queue.GetConsumingEnumerable())
            {
                if (_failure != null) continue;
                stdin.Write(data, 0, data.Length);
            }
            stdin.Flush();
        }
        catch (Exception ex)
        {
            Fail("FFmpegへの書き込みに失敗しました: " + ex.Message);
        }
        finally
        {
            try { _proc!.StandardInput.Close(); } catch (Exception) { }
        }
    }

    private void Fail(string message)
    {
        string detail;
        lock (_stderr) detail = _stderr.ToString().Trim();
        var full = detail.Length > 0 ? message + "\n" + detail : message;
        if (Interlocked.CompareExchange(ref _failureSet, 1, 0) != 0) return;
        _failure = full;
        Failed?.Invoke(full);
    }

    private int _failureSet;

    /// <summary>残りを書き切ってFFmpegを正常終了させる。UIスレッドで直接呼ばないこと。</summary>
    public void Finish()
    {
        _queue.CompleteAdding();
        _writer?.Join(TimeSpan.FromSeconds(30));
        if (_proc == null) return;
        if (!_proc.WaitForExit(30000))
        {
            try { _proc.Kill(true); } catch (Exception) { }
            Fail("FFmpegの終了待ちがタイムアウトしました");
        }
        else if (_proc.ExitCode != 0 && _failure == null)
        {
            Fail($"FFmpegがエラー終了しました（コード {_proc.ExitCode}）");
        }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch (Exception) { }
        _proc?.Dispose();
        _queue.Dispose();
    }
}
