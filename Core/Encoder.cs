using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Recorder.Core;

public enum OutputFormat { Flac, Mp3 }

/// <summary>
/// FFmpegを子プロセスとして起動し、標準入力へPCMを流して直接ファイルに書き出す。
/// 書き込みは専用スレッド＋キューで行い、録音側（キャプチャ）を止めない。
/// アプリが強制終了しても、FFmpegは標準入力の終端を検知して自分で終了する。
/// </summary>
public sealed class FfmpegEncoder : IDisposable
{
    private readonly Process _proc;
    private readonly BlockingCollection<byte[]> _queue = new(4000);
    private readonly Thread _writer;
    private readonly StringBuilder _log = new();
    private volatile bool _failed;
    private int _dropped;

    public bool Failed => _failed;
    public int DroppedBuffers => _dropped;

    public string LastLog
    {
        get { lock (_log) return _log.ToString(); }
    }

    /// <param name="inputFormat">FFmpegの入力形式名（f32le / s16le / s24le / s32le）</param>
    public FfmpegEncoder(string ffmpegPath, string outputPath, OutputFormat format,
        string inputFormat, int sampleRate, int channels)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var a in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", inputFormat, "-ar", sampleRate.ToString(), "-ac", channels.ToString(), "-i", "pipe:0",
        })
            psi.ArgumentList.Add(a);

        if (format == OutputFormat.Mp3)
        {
            foreach (var a in new[] { "-c:a", "libmp3lame", "-b:a", "320k", "-id3v2_version", "3" })
                psi.ArgumentList.Add(a);
            if (channels > 2)
            {
                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("2");
            }
        }
        else
        {
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("flac");
        }
        psi.ArgumentList.Add(outputPath);

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_log)
            {
                if (_log.Length < 4000) _log.AppendLine(e.Data);
            }
        };
        _proc.BeginErrorReadLine();

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "FfmpegWriter" };
        _writer.Start();
    }

    private void WriteLoop()
    {
        var stdin = _proc.StandardInput.BaseStream;
        try
        {
            foreach (var buf in _queue.GetConsumingEnumerable())
                stdin.Write(buf, 0, buf.Length);
            stdin.Flush();
        }
        catch (Exception)
        {
            _failed = true;
        }
        finally
        {
            try { stdin.Close(); } catch { }
        }
    }

    /// <summary>PCMを渡す（コピーしてキューへ）。失敗・満杯なら false。</summary>
    public bool Write(byte[] buffer, int count)
    {
        if (_failed || _queue.IsAddingCompleted) return false;
        var copy = new byte[count];
        Buffer.BlockCopy(buffer, 0, copy, 0, count);
        try
        {
            if (_queue.TryAdd(copy)) return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>残りを書き切ってFFmpegを正常終了させる。時間がかかるのでUIスレッド以外で呼ぶ。</summary>
    public void Finish()
    {
        try { _queue.CompleteAdding(); } catch { }
        _writer.Join(15000);
        if (!_proc.WaitForExit(15000))
        {
            _failed = true;
            try { _proc.Kill(); } catch { }
            _proc.WaitForExit(2000);
        }
        try
        {
            if (_proc.HasExited && _proc.ExitCode != 0) _failed = true;
        }
        catch { }
    }

    public void Dispose()
    {
        _proc.Dispose();
        _queue.Dispose();
    }
}
