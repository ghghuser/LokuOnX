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
            Loc.Get("recovery.confirm", files.Count),
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;

        status(Loc.Get("recovery.working"));
        var results = await Task.Run(() => Recovery.Repair(files, AppPaths.Ffmpeg));
        var lines = results.Take(5).Select(r => r.Path).ToList();
        var text = Loc.Get("recovery.done", results.Count) + "\n" + string.Join("\n", lines);
        if (results.Count > lines.Count) text += "\n" + Loc.Get("recovery.more", results.Count - lines.Count);
        var warnings = results.Where(r => r.Warning != null).Select(r => r.Warning!).Distinct().Take(3).ToList();
        if (warnings.Count > 0) text += "\n" + Loc.Get("recovery.warn", string.Join(" / ", warnings));
        status(text);
    }
}
