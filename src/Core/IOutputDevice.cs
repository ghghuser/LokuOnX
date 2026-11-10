namespace Recorder.Core;

/// <summary>出力先にできる再生デバイス1件。Id は "default"（既定の出力デバイス）または Windows のデバイスID（入力側 "render:{ID}" の ID と同じ）。</summary>
public sealed record OutputDeviceInfo(string Id, string Name)
{
    public const string DefaultId = "default";

    public bool IsDefault => Id == DefaultId;

    public override string ToString() => Name;
}

/// <summary>再生デバイス1台への出力。音づくりは OutputRenderer が行い、ここはデバイスを開いてコールバックから Render を呼ぶだけ（M11-2）。</summary>
public interface IOutputDevice : IDisposable
{
    /// <summary>デバイスを開いて再生を始める。factory にデバイスのサンプリング周波数とチャンネル数を渡すと OutputRenderer を作る。失敗したら理由、成功なら null。</summary>
    string? Start(Func<int, int, OutputRenderer> factory);

    OutputRenderer? Renderer { get; }

    /// <summary>始めたあとに、デバイスが外れるなどして止まったか。</summary>
    bool Failed { get; }

    string LastError { get; }
}

/// <summary>再生デバイスの一覧と、出力を作る口（Windows 以外の実装を後から差し込めるようにする。PROJECT.md §6-3）。</summary>
public interface IOutputDeviceProvider
{
    /// <summary>先頭は「既定の出力デバイス」（Id="default"）、続けて名前順。</summary>
    IReadOnlyList<OutputDeviceInfo> List();

    IOutputDevice Create(string id);
}
