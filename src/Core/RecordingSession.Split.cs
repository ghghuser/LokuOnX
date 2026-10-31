namespace Recorder.Core;

/// <summary>RecordingSession のうち、分割（トラックの切り替え）の部分。</summary>
public sealed partial class RecordingSession
{
    private static readonly object FinalizeLock = new();

    /// <summary>1つのファイル（トラック）。</summary>
    private sealed class Track
    {
        public Track(int index, string tempPath, FfmpegEncoder encoder, TimeSpan start)
        {
            Index = index;
            TempPath = tempPath;
            Encoder = encoder;
            Start = start;
        }

        public int Index { get; }
        public string TempPath { get; }
        public FfmpegEncoder Encoder { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; set; }
        public FinalizeResult? Result { get; set; }
        public string? Failure { get; set; }
    }

    // ---- 分割 ----
    /// <summary>手動で分割する。途切れずに次のファイルへ移る。直前の分割から1秒未満なら何もしない。UIスレッドを止めないよう Task.Run 経由で呼ぶこと。</summary>
    public bool Split() => RollOver(manual: true);

    private bool RollOver(bool manual)
    {
        lock (_splitLock)
        {
            if (_stopped) return false;
            var now = _clock.Elapsed;
            if (manual ? now - _splitter.TrackStart < TimeSpan.FromSeconds(1) : !_splitter.Due(now)) return false;

            var boundary = manual ? now : _splitter.NextSplitAt;
            var cut = Splitter.Floor(boundary);

            // 次のFFmpegを先に起動してから出力先を切り替える（起動中の音は標準入力のパイプに溜まる）
            Track? next = null;
            try
            {
                next = NewTrack(_current.Index + 1, cut);
                _tracks.Add(next);
                next.Encoder.Start();
            }
            catch (Exception ex)
            {
                if (next != null)
                {
                    _tracks.Remove(next);
                    next.Encoder.Dispose();
                }
                if (!manual) _splitter.Retry(now);
                Notice?.Invoke("分割できませんでした: " + ex.Message);
                return false;
            }

            Track old;
            lock (_sinkLock)
            {
                old = _current;
                old.End = cut;
                _current = next;
            }
            _splitter.Rolled(boundary);

            var task = Task.Run(() => Close(old, announce: true));
            lock (_closing) _closing.Add(task);
            return true;
        }
    }

    /// <summary>トラックのFFmpegを終わらせ、最終名へ確定する。</summary>
    private void Close(Track t, bool announce)
    {
        try
        {
            t.Encoder.Finish();
            t.Failure = t.Encoder.Failure;
            // 名前の確定は1つずつ（同名の取り合いを避ける）
            lock (FinalizeLock)
                t.Result = _plan.FinishTrack(t.TempPath, t.Index, t.Start, t.End, _ffmpeg);
            if (announce && t.Failure == null)
            {
                var msg = "保存しました: " + t.Result.Path;
                if (t.Result.Warning != null) msg += "（注意: " + t.Result.Warning + "）";
                Notice?.Invoke(msg);
            }
        }
        catch (Exception ex)
        {
            t.Failure ??= ex.Message;
        }
        finally
        {
            t.Encoder.Dispose();
        }
    }
}
