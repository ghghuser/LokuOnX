namespace Recorder.Core;

/// <summary>
/// 出力1台の録音（M13）。その出力の最終ミックスを OutputTapSource 経由で RecordingSession へ流す。
/// 形式・音質・保存先・ファイル名の規則は入力の録音と共通（設定を共有）。ファイル名には出力名を {Device} として付ける（パターンに無ければ自動で付く）。
/// メインの録音と一緒に始める場合は RecordingGroup が持ち、単独で始める場合は画面（MainWindow）が持つ。
/// </summary>
public sealed class OutputRecording : IDisposable
{
    /// <summary>出力の録音の仮名が、入力の録音（デバイスごと別ファイルの 0〜）と重ならないようにする番号の起点。</summary>
    public const int LaneBase = 10;

    private readonly OutputTapSource _tap;

    public RecordingSession Session { get; }
    public string Name { get; }
    public int Index { get; }

    /// <summary>失敗したとき（別スレッドから。出力の録音の失敗で、ほかの録音は止めない）。</summary>
    public event Action<string>? Error;
    public event Action<string>? Notice;
    public event Action<string>? Saved;

    private OutputRecording(OutputTapSource tap, RecordingSession session, string name, int index)
    {
        _tap = tap;
        Session = session;
        Name = name;
        Index = index;
        session.Error += m => Error?.Invoke(m);
        session.Notice += m => Notice?.Invoke(m);
        session.Saved += p => Saved?.Invoke(p);
        tap.Notice += m => Notice?.Invoke(m);
    }

    /// <summary>出力が動いていないとき（録れない）は null。保存先フォルダも作る。</summary>
    public static OutputRecording? Create(OutputEngine engine, string name, int index, Settings s, string ffmpegPath, DateTime now)
    {
        if (engine.Format is not { } f) return null;
        var tap = new OutputTapSource(f.Rate, f.Channels, engine.AttachRecorder);
        RecordingSession? session = null;
        try
        {
            var plan = RecordingPlan.Create(s, name, now, LaneBase + index, addDevice: true);
            Directory.CreateDirectory(plan.Folder);
            session = new RecordingSession(tap, ffmpegPath, plan, TimeSpan.FromSeconds(s.SplitSeconds));
        }
        catch
        {
            tap.Dispose();
            throw;
        }
        return new OutputRecording(tap, session, name, index);
    }

    public void Start() => Session.Start();

    /// <summary>止めてファイルを閉じ、名前を確定する。Task.Run 経由で呼ぶこと。</summary>
    public void Stop() => Session.Stop();

    public void Dispose() => Session.Dispose();
}
