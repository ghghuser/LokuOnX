namespace Recorder.Core;

/// <summary>
/// 分割のタイミング計算（PROJECT.md §3-3）。WPF や Windows API には依存しない（§6-3）。
/// 間隔が 0（なし）でも、1つのファイルは最長24時間で必ず分割する。
/// </summary>
public sealed class Splitter
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(24);

    private readonly TimeSpan _limit;

    /// <summary>現在のトラックが始まった録音経過時間。</summary>
    public TimeSpan TrackStart { get; private set; }

    /// <summary>次に自動で分割する録音経過時間。</summary>
    public TimeSpan NextSplitAt { get; private set; }

    /// <param name="interval">自動分割の間隔。0以下は「なし」（24時間の強制分割だけ）。</param>
    public Splitter(TimeSpan interval)
    {
        _limit = interval > TimeSpan.Zero && interval < MaxLength ? interval : MaxLength;
        NextSplitAt = _limit;
    }

    public bool Due(TimeSpan elapsed) => elapsed >= NextSplitAt;

    /// <summary>分割した。boundary が新しいトラックの開始。</summary>
    public void Rolled(TimeSpan boundary)
    {
        TrackStart = boundary;
        NextSplitAt = boundary + _limit;
    }

    /// <summary>自動分割に失敗したとき、少し後でやり直す。</summary>
    public void Retry(TimeSpan now) => NextSplitAt = now + TimeSpan.FromSeconds(10);

    /// <summary>ファイル名に使う時間は整数秒（切り捨て）。</summary>
    public static TimeSpan Floor(TimeSpan t) => TimeSpan.FromSeconds(Math.Floor(Math.Max(0, t.TotalSeconds)));
}
