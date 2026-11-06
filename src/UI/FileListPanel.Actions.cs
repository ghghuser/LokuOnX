using System.Diagnostics;
using System.Windows;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>ファイル一覧のボタン・右クリックの操作: 再生・エクスプローラーで開く・名前の変更・ごみ箱へ削除（PROJECT.md §3-7）。</summary>
public partial class FileListPanel
{
    private Window? Owner => Window.GetWindow(this);

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        Refresh();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (SelectedFiles.FirstOrDefault() is { } f) PlayRequested?.Invoke(f);
        else ShowError("再生するファイルを選んでください。");
    }

    // ---- エクスプローラーで開く（複数選択のときは確認してから） ----
    private void Explorer_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        var files = SelectedFiles;
        if (files.Count == 0)
        {
            ShowError("ファイルを選んでください。");
            return;
        }
        if (files.Count > 1)
        {
            var answer = MessageBox.Show(Owner!, $"選んだ {files.Count} 個のファイルを、それぞれエクスプローラーで開きます（{files.Count} 個のウィンドウが開きます）。よろしいですか？",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        foreach (var f in files)
        {
            try
            {
                if (File.Exists(f.Path))
                    Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{f.Path}\"", UseShellExecute = true });
            }
            catch (Exception ex) { ShowError("エクスプローラーを開けませんでした: " + ex.Message); return; }
        }
    }

    // ---- 名前の変更（1つだけ。再生中なら先に止める） ----
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        var files = SelectedFiles;
        if (files.Count != 1)
        {
            ShowError("名前を変えるファイルを1つだけ選んでください。");
            return;
        }
        var path = files[0].Path;
        var dialog = new RenameDialog(_settings.Theme, path) { Owner = Owner };
        if (dialog.ShowDialog() != true || dialog.Target is not { } target) return;

        Releasing?.Invoke(new[] { path });   // 再生中のファイルは名前を変えられないので、止めて手放す
        var error = FileOps.Rename(path, target);
        if (error != null) ShowError(error);
        else _settings.ReplaceInHistory(path, target);
        Refresh(error == null ? target : path);
    }

    // ---- ごみ箱へ削除（複数選択可。確認してから） ----
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        var files = SelectedFiles;
        if (files.Count == 0)
        {
            ShowError("削除するファイルを選んでください。");
            return;
        }
        var what = files.Count == 1 ? $"「{files[0].Name}」" : $"選んだ {files.Count} 個のファイル";
        var answer = MessageBox.Show(Owner!, $"{what}をごみ箱へ移します。よろしいですか？",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var paths = files.Select(f => f.Path).ToList();
        Releasing?.Invoke(paths);
        var failed = await Task.Run(() => RecycleBin.Send(paths));
        if (failed.Count > 0) ShowError("ごみ箱へ移せなかったファイル: " + string.Join(" / ", failed));
        _settings.RemoveFromHistory(paths.Where(p => !File.Exists(p)));
        Refresh();
    }

    // ---- 任意のフォルダを一覧に表示する（最近の10件に入る） ----
    /// <summary>フォルダの中身を一覧に表示する。フォルダは「最近の保存先」（10件まで）に入り、プルダウンで選べるようになる。</summary>
    public void ShowFolder(string folder)
    {
        ClearError();
        folder = folder.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            ShowError("フォルダが見つかりません: " + folder);
            return;
        }
        _settings.AddRecentFolder(folder);
        ReloadSources(_settings.RecentFolders.FirstOrDefault());
        Refresh();
    }

    private void ShowSaveFolder_Click(object sender, RoutedEventArgs e) => ShowFolder(_settings.Folder);

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var start = IsHistory ? _settings.Folder : SourceKey;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "一覧に表示するフォルダを選んでください" };
        if (Directory.Exists(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(Owner) == true) ShowFolder(dlg.FolderName);
    }

    // ---- 履歴を空にする（ファイルは消さない。確認してから） ----
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (!IsHistory)
        {
            ReloadSources(HistoryKey);   // フォルダ表示中は、録音履歴に戻す
            Refresh();
            return;
        }
        var answer = MessageBox.Show(Owner!, "録音履歴の一覧を空にします（ファイルは消えません）。よろしいですか？",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _settings.ClearHistory();
        Refresh();
    }
}
