using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>タグ（タイトル・アーティスト・アルバム・ジャンル・年・コメント）とトラック番号タグ。入力欄を1か所にまとめる（PROJECT.md §3-5）。値は Settings に即保存。</summary>
public partial class TagPanel : UserControl
{
    private Settings? _s;
    private bool _loading;

    public TagPanel() => InitializeComponent();

    public void Init(Settings settings)
    {
        _s = settings;
        Reload();
    }

    /// <summary>設定の値を画面へ読み直す（プロファイルを読み込んだとき）。</summary>
    public void Reload()
    {
        if (_s == null) return;
        _loading = true;
        var t = _s.Tags;
        TitleBox.Text = t.Title;
        ArtistBox.Text = t.Artist;
        AlbumBox.Text = t.Album;
        GenreBox.Text = t.Genre;
        YearBox.Text = t.Year;
        CommentBox.Text = t.Comment;
        TrackCheck.IsChecked = t.TrackNumber;
        _loading = false;
    }

    /// <summary>画面の入力を設定へ反映する（録音開始の直前にも呼ぶ）。</summary>
    public void Commit()
    {
        if (_s == null || _loading) return;
        var t = new TagInfo(TitleBox.Text.Trim(), ArtistBox.Text.Trim(), AlbumBox.Text.Trim(),
            GenreBox.Text.Trim(), YearBox.Text.Trim(), CommentBox.Text.Trim(), TrackCheck.IsChecked == true);
        if (t != _s.Tags) _s.Tags = t;
    }

    private void Tag_LostFocus(object sender, RoutedEventArgs e) => Commit();

    private void Track_Changed(object sender, RoutedEventArgs e) => Commit();
}
