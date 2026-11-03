using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// ファイル一覧（PROJECT.md §3-7）。保存先フォルダ（日付フォルダの中も）の音声ファイルを新しい順に出す。
/// 一覧の読み込み・並べ替え・長さの取得は、このファイル。ボタンの操作（開く・名前の変更・削除）は FileListPanel.Actions.cs。
/// </summary>
public partial class FileListPanel : UserControl
{
    private static readonly string[] Titles = { "ファイル名", "長さ", "サイズ", "作成日時" };
    private static readonly string[] SortKeys = { "Name", "LengthSeconds", "Size", "Created" };
    private const int AutoLengthLimit = 200;   // 長さを自動で調べるのは新しい順にこの数まで（選んだものは必ず調べる）

    private Settings _settings = null!;
    private Func<string> _folder = () => "";
    private MediaInfo _info = null!;
    private List<LibraryFile> _files = new();
    private CancellationTokenSource? _cts;
    private int _scanId;
    private bool _restoring;
    private string _sortKey = "Created";
    private bool _sortDesc = true;

    /// <summary>ちょうど1つ選んだとき。</summary>
    public event Action<LibraryFile>? Selected;

    /// <summary>ダブルクリック／Enter／「再生」。</summary>
    public event Action<LibraryFile>? PlayRequested;

    /// <summary>名前の変更・削除の直前（再生中のファイルを手放すため）。</summary>
    public event Action<IReadOnlyCollection<string>>? Releasing;

    public FileListPanel()
    {
        InitializeComponent();
    }

    public void Init(Settings settings, Func<string> folder, string ffmpegPath)
    {
        _settings = settings;
        _folder = folder;
        _info = new MediaInfo(ffmpegPath);
        UpdateHeaders();
    }

    private List<LibraryFile> SelectedFiles => List.SelectedItems.Cast<LibraryFile>().ToList();

    /// <summary>保存先を読み直す。選んでいたファイル（keepPath があればそれ）は、見つかれば選び直す。</summary>
    public async void Refresh(string? keepPath = null)
    {
        var id = ++_scanId;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var keep = keepPath != null ? new List<string> { keepPath } : SelectedFiles.Select(f => f.Path).ToList();
        var known = _files.ToDictionary(f => f.Path, f => f);
        var folder = _folder();

        var files = await Task.Run(() => AudioLibrary.Scan(folder));
        if (id != _scanId) return;
        foreach (var f in files)   // 調べ済みの長さは引き継ぐ（同じサイズのときだけ）
            if (known.TryGetValue(f.Path, out var old) && old.Size == f.Size) f.Length = old.Length;

        _files = files;
        _restoring = true;
        List.ItemsSource = files;
        ApplySort();
        foreach (var f in files.Where(f => keep.Contains(f.Path, StringComparer.OrdinalIgnoreCase)))
            List.SelectedItems.Add(f);
        _restoring = false;
        CountText.Text = files.Count == 0 ? "ファイルはありません" : $"{files.Count} 個";
        RaiseSelected();
        _ = LoadLengthsAsync(files.Where(f => f.Length == null).Take(AutoLengthLimit).ToList(), cts.Token);
    }

    /// <summary>長さを2本並行で調べる（FFmpegを1ファイルずつ起動するため、新しい順に一定数まで）。</summary>
    private async Task LoadLengthsAsync(List<LibraryFile> files, CancellationToken ct)
    {
        try
        {
            var opt = new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct };
            await Parallel.ForEachAsync(files, opt, async (f, token) =>
            {
                var length = await Task.Run(() => _info.Get(f.Path), token);
                await Dispatcher.InvokeAsync(() => { f.Length = length; });
            });
        }
        catch (OperationCanceledException) { }
    }

    private async void EnsureLength(LibraryFile f)
    {
        if (f.Length != null) return;
        var length = await Task.Run(() => _info.Get(f.Path));
        f.Length = length;
    }

    // ---- 選択 ----
    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_restoring) RaiseSelected();
    }

    private void RaiseSelected()
    {
        if (List.SelectedItems.Count != 1 || List.SelectedItem is not LibraryFile f) return;
        EnsureLength(f);
        Selected?.Invoke(f);
    }

    private void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(List, src) is not ListViewItem) return;
        if (List.SelectedItem is LibraryFile f) PlayRequested?.Invoke(f);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2: Rename_Click(sender, e); e.Handled = true; break;
            case Key.Delete: Delete_Click(sender, e); e.Handled = true; break;
            case Key.Enter: Play_Click(sender, e); e.Handled = true; break;
        }
    }

    // ---- 並べ替え（見出しのクリック。もう一度で逆順） ----
    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } col, Role: not GridViewColumnHeaderRole.Padding }) return;
        if (List.View is not GridView gv) return;
        var index = gv.Columns.IndexOf(col);
        if (index < 0) return;
        // 同じ列をもう一度押すと逆順。別の列は、名前だけ昇順・ほかは大きい／新しい順から
        _sortDesc = SortKeys[index] == _sortKey ? !_sortDesc : SortKeys[index] != "Name";
        _sortKey = SortKeys[index];
        ApplySort();
    }

    private void ApplySort()
    {
        var view = CollectionViewSource.GetDefaultView(List.ItemsSource);
        if (view != null)
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(_sortKey, _sortDesc ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }
        UpdateHeaders();
    }

    private void UpdateHeaders()
    {
        if (List.View is not GridView gv) return;
        for (var i = 0; i < gv.Columns.Count && i < Titles.Length; i++)
            gv.Columns[i].Header = Titles[i] + (SortKeys[i] == _sortKey ? (_sortDesc ? " ▼" : " ▲") : "");
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
