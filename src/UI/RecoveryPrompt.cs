using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>起動時に、前回正しく閉じられなかった録音ファイルを探し、確認のうえ整える（PROJECT.md §3-1）。</summary>
public static class RecoveryPrompt
{
    /// <summary>見つからなければ何も出さない。2つ目の起動（設定を書かない側）では何もしない。</summary>
    public static async Task RunAsync(Window owner, Settings settings, Func<bool> busy, Action<string> status)
    {
        if (settings.ReadOnly || !File.Exists(AppPaths.Ffmpeg)) return;
        var files = await Task.Run(() => Recovery.Find(settings.Folder));
        if (files.Count == 0 || busy()) return;

        var answer = MessageBox.Show(owner,
            $"前回、正しく閉じられなかった録音ファイルが {files.Count} 個見つかりました。\n名前を付けて整えますか？\n\n"
            + "「いいえ」の場合はそのまま残し、次回の起動時にもう一度確認します。",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;

        status("録音ファイルを整えています...");
        var results = await Task.Run(() => Recovery.Repair(files, AppPaths.Ffmpeg));
        var lines = results.Take(5).Select(r => r.Path).ToList();
        var text = $"{results.Count} 個のファイルを整えました:\n" + string.Join("\n", lines);
        if (results.Count > lines.Count) text += $"\n…ほか {results.Count - lines.Count} 個";
        var warnings = results.Where(r => r.Warning != null).Select(r => r.Warning!).Distinct().Take(3).ToList();
        if (warnings.Count > 0) text += "\n注意: " + string.Join(" / ", warnings);
        status(text);
    }
}
