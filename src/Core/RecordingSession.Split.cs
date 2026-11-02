namespace Recorder.Core;

/// <summary>RecordingSession のうち、分割（トラックの切り替え）の部分。</summary>
public sealed partial class RecordingSession
{
    private static readonly object FinalizeLock = new();

    /// <summary>1つのファイル（トラック）。</summary>
    private sealed class Track
    {
        public Track(int index, string tempPath, FfmpegEncoder encoder, TimeSpan start, DateTime time)
        {
            Index = index;
            TempPath = tempPath;
            Encoder = encoder;
            Start = start;
            Time = time;
        }

        public int Index { get; }
        public string TempPath { get; }
        public FfmpegEncoder Encoder { get; }
        public TimeSpan Start { get; }
        /// <summary>このファイルを始めた実際の時刻（ファイル名の {Date}/{Time} 用）。</summary>
        public DateTime Time { get; }
        public TimeSpan End { get; set; }
        public FinalizeResult? Result { get; set; }
        public string? Failure { get; set; }
        /// <summary>このファイルへ書いたPCMのバイト数（実際の音声の長さの確認用）。</summary>
        public long Bytes { get; set; }
    }

    // ---- 分割 ----
    private volatile Track? _pending;   // 準備済みで、切り替えを待っている次のトラック

    /// <summary>手動で分割する。途切れずに次のファイルへ移る。直前の分割から1秒未満なら何もしない。UIスレッドを止めないよう Task.Run 経由で呼ぶこと。</summary>
    public bool Split()
    {
        lock (_splitLock)
        {
            if (_stopped) return false;
            var now = _clock.Elapsed;
            if (now - _splitter.TrackStart < TimeSpan.FromSeconds(1)) return false;
            CancelPending();   // 自動分割の準備中なら取り消して、手動を優先する
            var next = Prepare(now, DateTime.Now);
            if (next == null) return false;
            _filler.CutNow(() => Swap(next, now));
            return true;
        }
    }

    /// <summary>自動分割の準備（Tick から。境界の1秒前から）。次のFFmpegを先に起動し、境界ちょうどのフレームで切り替わるよう予約する。</summary>
    private void PrepareAuto(TimeSpan now)
    {
        lock (_splitLock)
        {
            if (_stopped || _pending != null) return;
            var boundary = _splitter.NextSplitAt;
            var wall = DateTime.Now + (boundary - now);   // 境界の実時刻（ファイル名の {Date}/{Time} 用）
            var next = Prepare(boundary, wall);
            if (next == null) { _splitter.Retry(now); return; }
            _pending = next;
            _filler.ScheduleCut(_filler.FramesFor(boundary), () => Swap(next, boundary));
        }
    }

    /// <summary>次のトラックを作ってFFmpegを起動する（起動中の音は標準入力のパイプに溜まる）。</summary>
    private Track? Prepare(TimeSpan boundary, DateTime wall)
    {
        Track? next = null;
        try
        {
            next = NewTrack(_current.Index + 1, Splitter.Floor(boundary), wall);
            _tracks.Add(next);
            next.Encoder.Start();
            return next;
        }
        catch (Exception ex)
        {
            if (next != null)
            {
                _tracks.Remove(next);
                next.Encoder.Dispose();
            }
            Notice?.Invoke("分割できませんでした: " + ex.Message);
            return null;
        }
    }

    /// <summary>切り替え（SilenceFiller のロック内で呼ばれる）。前のファイルを閉じる作業は別タスクへ。</summary>
    private void Swap(Track next, TimeSpan boundary)
    {
        Track old;
        lock (_sinkLock)
        {
            old = _current;
            old.End = Splitter.Floor(boundary);
            _current = next;
        }
        _splitter.Rolled(boundary);
        _pending = null;
        var task = Task.Run(() => Close(old, announce: true));
        lock (_closing) _closing.Add(task);
    }

    /// <summary>切り替え待ちの次のトラックを捨てる（停止時・手動分割時）。_splitLock を持って呼ぶ。</summary>
    private void CancelPending()
    {
        var p = _pending;
        if (p == null) return;
        if (!_filler.CancelCut()) return;   // すでに切り替わっていた
        _pending = null;
        _tracks.Remove(p);
        p.Encoder.Abort();
        // FFmpegがファイルを放すまで少し待ってから消す（消し損ねると _rec_ のファイルが残る）
        for (int i = 0; i < 20; i++)
        {
            try { if (File.Exists(p.TempPath)) File.Delete(p.TempPath); return; }
            catch (Exception) { Thread.Sleep(100); }
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
                t.Result = _plan.FinishTrack(t.TempPath, t.Index, t.Time, t.Start, t.End, _ffmpeg);
            if (announce && t.Failure == null)
            {
                var msg = "保存しました: " + t.Result.Path + $"（音声 {SecondsOf(t):0.00}秒）";
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
