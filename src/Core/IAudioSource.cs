namespace Recorder.Core;

/// <summary>録音デバイス1件（一覧表示用）。Key は "capture:{ID}" または "render:{ID}"。</summary>
public sealed record AudioDeviceInfo(string Key, string Label, bool IsLoopback, int SampleRate, int Channels,
    bool IsDefault = false)
{
    /// <summary>一覧に出す名前。Windowsの既定のデバイスには末尾に ＊（全角）を付ける（ファイル名の {Device} には付けない）。</summary>
    public string DisplayLabel => IsDefault ? Label + DefaultMark : Label;

    /// <summary>ラベル先頭の絵文字（🎤・🔊・🎛）。無ければ空。一覧で、アイコンだけ色を付けるのに使う（Core は色を持たない）。</summary>
    public string Icon => Label.Length > 2 && char.IsHighSurrogate(Label[0]) && Label[2] == ' ' ? Label.Substring(0, 2) : "";

    /// <summary>アイコンのあとの部分（先頭の空白を含む）。アイコンが無ければラベル全体。</summary>
    public string LabelAfterIcon => Icon.Length == 0 ? Label : Label.Substring(2);

    /// <summary>既定のデバイスの目印（全角の ＊。一覧で赤紫に色を付ける）。</summary>
    public string DefaultMark => IsDefault ? " ＊" : "";

    /// <summary>ダーク用のコンボボックスなど、表示名を指定できない場面でも名前が出るようにする。</summary>
    public override string ToString() => DisplayLabel;
}

/// <summary>音声データの形式（PCM）。</summary>
public sealed record AudioFormatInfo(int SampleRate, int Channels, int BitsPerSample, bool IsFloat);

/// <summary>音声入力の抽象。Windows(WASAPI)以外の実装を後から差し込めるようにする（PROJECT.md §6-3）。</summary>
public interface IAudioSource : IDisposable
{
    AudioFormatInfo Format { get; }

    /// <summary>録音スレッドから呼ばれる。引数は（PCMバイト列のコピー, 有効バイト数）。</summary>
    event Action<byte[], int>? DataAvailable;

    /// <summary>入力が止まったとき（正常停止は null、異常は例外）。</summary>
    event Action<Exception?>? Stopped;

    void Start();

    /// <summary>入力を止め、残りのデータを流し終えてから戻る。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    void Stop();
}

/// <summary>デバイス一覧の取得と、選んだデバイスの入力を作る口。</summary>
public interface IAudioDeviceProvider
{
    /// <summary>マイク（入力）→スピーカー等（出力のループバック）の順。それぞれ名前順。各グループの先頭は「既定のデバイスに追従」の擬似デバイス。</summary>
    IReadOnlyList<AudioDeviceInfo> List();

    /// <summary>Windowsの既定の入力デバイスの Key。取得できなければ null。</summary>
    string? DefaultCaptureKey();

    /// <summary>Windowsの既定の出力デバイス（ループバック用）の Key。取得できなければ null。</summary>
    string? DefaultRenderKey();

    /// <summary>つながっているデバイスと既定の組み合わせを表す文字列。変わったら一覧を読み直す目安（軽い）。</summary>
    string Signature();

    IAudioSource Create(string key);
}
