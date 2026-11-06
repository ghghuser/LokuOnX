using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 録音履歴・ファイル一覧（PROJECT.md §3-7）。「表示」のプルダウンで、録音履歴（空にするまで残る）か、
/// 最近の保存先フォルダ（10件まで）の中身を切り替える。一覧の読み込み・並べ替え・長さの取得は、このファイル。
/// ボタンの操作（開く・名前の変更・削除・履歴を空にする）は FileListPanel.Actions.cs。
/// </summary>
public partial class FileListPanel : UserControl
{
    private sealed record Source(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly string[] Titles = { "ファイル名", "長さ", "サイズ", "作成日時" };
    private static readonly string[] SortKeys = { "Name", "LengthSeconds", "Size", "Created" };
    private const int AutoLengthLimit = 200;   // 長さを自動で調べるのは新しい順にこの数まで（選んだものは必ず調べる）
    private const string HistoryKey = "";

    private Settings _settings = null!;
    private MediaInfo _info = null!;
    private ObservableCollection<LibraryFile> _items = new();
    private LibraryFile? _recording;
    private CancellationTokenSource? _cts;
    private int _scanId;
    private bool _restoring;
    private bool _loadingSources;
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

    /// <summary>一覧の下に置く部品（プレイヤー）。</summary>
    public object? Footer
    {
        get => Slot.Content;
        set => Slot.Content = value;
    }

    public void Init(Settings settings, string ffmpegPath)
    {
        _settings = settings;
        _info = new MediaInfo(ffmpegPath);
        ReloadSources();
        UpdateHeaders();
    }

    private string SourceKey => (SourceBox.SelectedItem as Source)?.Key ?? HistoryKey;
    private bool IsHistory => SourceKey == HistoryKey;
    private List<LibraryFile> SelectedFiles => List.SelectedItems.Cast<LibraryFile>().Where(f => !f.IsRecording).ToList();

    /// <summary>プルダウン（録音履歴＋最近の保存先）を作り直す。いま選んでいる場所はできるだけ保つ。</summary>
    public void ReloadSources(string? select = null)
    {
        var keep = select ?? SourceKey;
        _loadingSources = true;
        var sources = new List<Source> { new(HistoryKey, "録音履歴") };
        sources.AddRange(_settings.RecentFolders.Select(f => new Source(f, "📁 " + f)));
        SourceBox.ItemsSource = sources;
        SourceBox.SelectedItem = sources.FirstOrDefault(s => string.Equals(s.Key, keep, StringComparison.OrdinalIgnoreCase)) ?? sources[0];
        _loadingSources = false;
        UpdateClearButton();
    }

    /// <summary>録音履歴を表示中は「履歴を空にする」、フォルダを表示中は「録音履歴」（押すと録音履歴に戻る）。</summary>
    private void UpdateClearButton() => ClearButton.Content = IsHistory ? "履歴を空にする" : "録音履歴";

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSources || _settings == null) return;
        UpdateClearButton();
        ClearError();
        Refresh();
    }

    /// <summary>表示を読み直す。選んでいたファイル（keepPath があればそれ）は、見つかれば選び直す。</summary>
    public async void Refresh(string? keepPath = null)
    {
        var id = ++_scanId;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var keep = keepPath != null ? new List<string> { keepPath } : SelectedFiles.Select(f => f.Path).ToList();
        var known = _items.Where(f => !f.IsRecording).ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        var key = SourceKey;
        var history = key == HistoryKey ? _settings.History.ToList() : null;

        var files = await Task.Run(() => history != null ? AudioLibrary.FromPaths(history) : AudioLibrary.Scan(key));
        if (id != _scanId) return;
        foreach (var f in files)   // 調べ済みの長さは引き継ぐ（同じサイズのときだけ）
            if (known.TryGetValue(f.Path, out var old) && old.Size == f.Size) f.Length = old.Length;

        _restoring = true;
        _items = new ObservableCollection<LibraryFile>(files);
        if (_recording != null && RecordingShown()) _items.Insert(0, _recording);
        List.ItemsSource = _items;
        ApplySort();
        foreach (var f in files.Where(f => keep.Contains(f.Path, StringComparer.OrdinalIgnoreCase)))
            List.SelectedItems.Add(f);
        _restoring = false;
        CountText.Text = files.Count == 0 ? "（なし）" : $"{files.Count} 個";
        RaiseSelected();
        _ = LoadLengthsAsync(files.Where(f => f.Length == null).Take(AutoLengthLimit).ToList(), cts.Token);
    }

    /// <summary>録音中の行は、録音履歴と、いまの保存先を開いているときに出す。</summary>
    private bool RecordingShown() =>
        IsHistory || string.Equals(SourceKey.TrimEnd('\\', '/'), _settings.Folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>録音中は、一覧の一番上に「録音中…」の行を出す（ファイルではない仮の行）。null で消す。</summary>
    public void SetRecording(string? text)
    {
        if (text == null)
        {
            if (_recording != null) _items.Remove(_recording);
            _recording = null;
            return;
        }
        if (_recording == null)
        {
            _recording = LibraryFile.Recording(text);
            if (RecordingShown()) _items.Insert(0, _recording);
            return;
        }
        _recording.SetName(text);
        if (!_items.Contains(_recording) && RecordingShown()) _items.Insert(0, _recording);
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
        var files = SelectedFiles;
        if (files.Count != 1) return;
        EnsureLength(files[0]);
        Selected?.Invoke(files[0]);
    }

    private void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(List, src) is not ListViewItem) return;
        if (List.SelectedItem is LibraryFile { IsRecording: false } f) PlayRequested?.Invoke(f);
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

    // ---- 並べ替え（見出しのクリック。もう一度で逆順。録音中の行は常に一番上） ----
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
        if (CollectionViewSource.GetDefaultView(List.ItemsSource) is ListCollectionView view)
            view.CustomSort = new LibrarySort(_sortKey, _sortDesc);
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
        HintText.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Visible;
    }
}
