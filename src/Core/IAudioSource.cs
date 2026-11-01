namespace Recorder.Core;

/// <summary>録音デバイス1件（一覧表示用）。Key は "capture:{ID}" または "render:{ID}"。</summary>
public sealed record AudioDeviceInfo(string Key, string Label, bool IsLoopback, int SampleRate, int Channels,
    bool IsDefault = false)
{
    /// <summary>一覧に出す名前。Windowsの既定のデバイスには末尾に ＊（全角）を付ける（ファイル名の {Device} には付けない）。</summary>
    public string DisplayLabel => IsDefault ? Label + " ＊" : Label;
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
    /// <summary>マイク（入力）→スピーカー等（出力のループバック）の順。それぞれ名前順。</summary>
    IReadOnlyList<AudioDeviceInfo> List();

    /// <summary>Windowsの既定の入力デバイスの Key。取得できなければ null。</summary>
    string? DefaultCaptureKey();

    IAudioSource Create(string key);
}
