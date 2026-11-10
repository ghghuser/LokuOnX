namespace Recorder.Core;

/// <summary>
/// 入力デバイス1台から出力へ音を送る「複製先」の取っ手（M11-2）。
/// 実際のバッファ（SourceBuffer）は、入力の形式（サンプリング周波数・チャンネル数）が変わるたびに DeviceStream が作り直すので、
/// 出力側（OutputRenderer）は毎回この取っ手から最新のバッファを読む。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class InputTap
{
    /// <summary>いまの複製先バッファ（まだ音が届いていない間は null）。</summary>
    public volatile SourceBuffer? Buffer;
}
