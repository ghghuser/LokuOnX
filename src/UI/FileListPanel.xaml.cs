using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 録音履歴・ファイル一覧（PROJECT.md §3-7）。プルダウンで、録音履歴（空にするまで残る）か、
/// 最近の保存先フォルダ（10件まで）の中身を切り替える。一覧の読み込み・並べ替え・長さの取得は、このファイル。
/// ボタンの操作（開く・名前の変更・削除・履歴を空にする）は FileListPanel.Actions.cs。
/// </summary>
public partial class FileListPanel : UserControl
{
    private sealed record Source(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static string[] Titles => new[] { Loc.Get("filelist.col.name"), Loc.Get("filelist.col.length"), Loc.Get("filelist.col.size"), Loc.Get("filelist.col.created") };
    private static readonly string[] SortKeys = { "Name", "LengthSeconds", "Size", "Created" };
    private const int AutoLengthLimit = 200;   // 長さを自動で調べるのは新しい順にこの数まで（選んだものは必ず調べる）
    private const string HistoryKey = "";

    private Settings _settings = null!;
    private MediaInfo _info = null!;
    private ObservableCollection<LibraryFile> _items = new();
    private readonly List<LibraryFile> _recordings = new();
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

    /// <summary>画面下のログ欄に出す知らせ（「タグを書き込みました」など。赤文字のエラーではないもの）。</summary>
    public event Action<string>? Notice;

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

    private string _ffmpeg = "";

    public void Init(Settings settings, string ffmpegPath)
    {
        _settings = settings;
        _info = new MediaInfo(ffmpegPath);
        _ffmpeg = ffmpegPath;
        ReloadSources();
        UpdateHeaders();
    }

    /// <summary>録音履歴のタブか（もう1つは「フォルダ」のタブ）。</summary>
    private bool IsHistory => SourceTabs.SelectedIndex != 1;

    /// <summary>いま表示しているもの。録音履歴なら HistoryKey、フォルダのタブならプルダウンで選んだフォルダ。</summary>
    private string SourceKey => IsHistory ? HistoryKey : (SourceBox.SelectedItem as Source)?.Key ?? _settings?.Folder ?? "";
    private List<LibraryFile> SelectedFiles => List.SelectedItems.Cast<LibraryFile>().Where(f => !f.IsRecording).ToList();

    /// <summary>フォルダのプルダウン（最近の保存先）を作り直す。select があればそれを表示する（HistoryKey なら録音履歴のタブ）。なければ、いま表示している場所をできるだけ保つ。</summary>
    /// <summary>タブの行の右端に置く要素（予約録音のお知らせ）。</summary>
    public object? TabsTrailing
    {
        get => TabsTrailingHost.Content;
        set => TabsTrailingHost.Content = value;
    }

    public void ReloadSources(string? select = null)
    {
        var keep = select ?? SourceKey;
        _loadingSources = true;
        var sources = _settings.RecentFolders.Select(f => new Source(f, "📁 " + f)).ToList();
        SourceBox.ItemsSource = sources;
        SourceBox.SelectedItem = sources.FirstOrDefault(s => string.Equals(s.Key, keep, StringComparison.OrdinalIgnoreCase))
                                 ?? sources.FirstOrDefault();
        SourceTabs.SelectedIndex = keep == HistoryKey ? 0 : 1;
        _loadingSources = false;
        UpdateToolbar();
    }

    /// <summary>録音履歴のタブでは「履歴を空にする」、フォルダのタブではフォルダの選択とボタンを出す。</summary>
    private void UpdateToolbar()
    {
        bool history = IsHistory;
        ClearButton.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        var folder = history ? Visibility.Collapsed : Visibility.Visible;
        SourceBox.Visibility = SaveFolderButton.Visibility = PickFolderButton.Visibility = folder;
    }

    private void SourceTab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != SourceTabs || _loadingSources || _settings == null) return;
        UpdateToolbar();
        ClearError();
        Refresh();
    }

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSources || _settings == null) return;
        ClearError();
        Refresh();
    }

    /// <summary>表示を読み直す。選んでいたファイル（keepPath があればそれ）は、見つかれば選び直す。</summary>
    public void Refresh(string? keepPath = null) => _ = RefreshAsync(keepPath);   // 呼び出し側は待たない（読み直しは裏で進む）

    private async Task RefreshAsync(string? keepPath)
    {
        try { await RefreshCoreAsync(keepPath); }
        catch (OperationCanceledException) { }   // 新しい読み直しに置き換えられた
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task RefreshCoreAsync(string? keepPath)
    {
        var id = ++_scanId;
        // 状態は先に集めておく（このあと待つ間に画面が変わっても、読み直しを始めた時点の選択・一覧で処理する）
        var keep = keepPath != null ? new List<string> { keepPath } : SelectedFiles.Select(f => f.Path).ToList();
        var known = _items.Where(f => !f.IsRecording).ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        var key = SourceKey;
        var history = key == HistoryKey ? _settings.History.ToList() : null;
        var previous = _cts;
        var cts = _cts = new CancellationTokenSource();
        if (previous != null)   // 前の読み直しは止めて、片付ける
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        var files = await Task.Run(() => history != null ? AudioLibrary.FromPaths(history) : AudioLibrary.Scan(key), cts.Token);
        if (id != _scanId) return;
        foreach (var f in files)   // 調べ済みの長さは引き継ぐ（同じサイズのときだけ）
            if (known.TryGetValue(f.Path, out var old) && old.Size == f.Size) f.Length = old.Length;

        _restoring = true;
        _items = new ObservableCollection<LibraryFile>(files);
        if (RecordingShown()) for (int k = 0; k < _recordings.Count; k++) _items.Insert(k, _recordings[k]);
        List.ItemsSource = _items;
        ApplySort();
        ApplyFilter();   // 絞り込み中なら、読み直したあとも同じ条件で（件数の表示もここで更新）
        foreach (var f in files.Where(f => keep.Contains(f.Path, StringComparer.OrdinalIgnoreCase) && List.Items.Contains(f)))
            List.SelectedItems.Add(f);
        _restoring = false;
        RaiseSelected();
        _ = LoadLengthsAsync(files.Where(f => f.Length == null).Take(AutoLengthLimit).ToList(), cts.Token);
    }

    /// <summary>録音中の行は、録音履歴と、いまの保存先を開いているときに出す。</summary>
    private bool RecordingShown() =>
        IsHistory || string.Equals(SourceKey.TrimEnd('\\', '/'), _settings.Folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>録音中は、一覧の一番上に「録音中…」の行を、録音中のファイルの数だけ出す（ファイルではない仮の行）。空で消す。</summary>
    public void SetRecordings(IReadOnlyList<string> texts)
    {
        while (_recordings.Count > texts.Count)
        {
            _items.Remove(_recordings[^1]);
            _recordings.RemoveAt(_recordings.Count - 1);
        }
        for (int k = 0; k < texts.Count; k++)
        {
            if (k < _recordings.Count) { _recordings[k].SetName(texts[k]); continue; }
            var row = LibraryFile.Recording(texts[k]);
            _recordings.Add(row);
            if (RecordingShown()) _items.Insert(Math.Min(k, _items.Count), row);
        }
        if (RecordingShown())
            foreach (var r in _recordings)
                if (!_items.Contains(r)) _items.Insert(0, r);
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

    private async Task EnsureLengthAsync(LibraryFile f)
    {
        if (f.Length != null) return;
        var length = await Task.Run(() => _info.Get(f.Path), CancellationToken.None);   // 選んだファイルの長さは、読み直しで止めない
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
        _ = EnsureLengthAsync(files[0]);
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

    // ---- 絞り込み（ファイル名に含まれる文字。数千件になっても探せるように） ----
    private string _filter = "";

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        _filter = FilterBox.Text.Trim();
        ApplyFilter();
    }

    /// <summary>絞り込みを一覧へ反映して、件数を更新する。録音中の仮の行は、絞り込みに関係なく残す。</summary>
    private void ApplyFilter()
    {
        if (CollectionViewSource.GetDefaultView(List.ItemsSource) is not ListCollectionView view) return;
        var text = _filter;
        view.Filter = text.Length == 0
            ? null
            : o => o is LibraryFile f && (f.IsRecording || f.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase));
        int total = _items.Count(f => !f.IsRecording);
        int shown = text.Length == 0 ? total : List.Items.Cast<LibraryFile>().Count(f => !f.IsRecording);
        CountText.Text = total == 0 ? Loc.Get("filelistpanel.s01") : text.Length == 0 ? Loc.Get("filelistpanel.s02", total) : Loc.Get("filelistpanel.s03", shown, total);
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
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
