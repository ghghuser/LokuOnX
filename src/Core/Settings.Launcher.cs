namespace Recorder.Core;

/// <summary>Settings のうち、M17 の連携（アプリランチャーと制御API）。プロファイルには含めない。</summary>
public sealed partial class Settings
{
    /// <summary>アプリランチャーに登録したアプリのパス（最大 <see cref="LauncherList.Max"/> 個。ini の Launcher1〜5）。</summary>
    public IReadOnlyList<string> LauncherPaths
    {
        get
        {
            var raw = new List<string?>();
            for (int i = 1; i <= LauncherList.Max; i++) raw.Add(Get("Launcher" + i, ""));
            return LauncherList.Clean(raw);
        }
        set
        {
            var clean = LauncherList.Clean(value);
            for (int i = 1; i <= LauncherList.Max; i++)
            {
                var text = i <= clean.Count ? clean[i - 1] : "";
                if (Get("Launcher" + i, "") != text) Set("Launcher" + i, text);   // 変わったものだけ書く（Set のたびに保存するため）
            }
        }
    }

    /// <summary>制御API（名前付きパイプ）を有効にする（既定OFF。ONのあいだだけ待ち受ける。同じ Windows ユーザーのプロセスだけが接続できる）。</summary>
    public bool ControlApiOn { get => Flag("ControlApi", false); set => SetFlag("ControlApi", value); }
}
