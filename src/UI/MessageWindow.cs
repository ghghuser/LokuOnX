using System.Windows.Interop;

namespace Recorder.UI;

/// <summary>
/// 目に見えない受信専用のウィンドウ。タスクトレイのアイコンとホットキーのメッセージを受ける。
/// メイン画面を隠していても・閉じていても動くよう、メイン画面とは別に1つ持つ。UIスレッドで作ること。
/// （通常のトップレベルの非表示ウィンドウにしているのは、メニューを正しく閉じるための前面化と、
/// エクスプローラー再起動の通知「TaskbarCreated」を受けるため。メッセージ専用ウィンドウは両方できない）
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private const int WsExToolWindow = 0x80;   // Alt+Tab に出さない
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("LokuOnX.MessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = WsExToolWindow,
        };
        _source = new HwndSource(p);
    }

    public IntPtr Handle => _source.Handle;

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    public void Dispose() => _source.Dispose();
}
