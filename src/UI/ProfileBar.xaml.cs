using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>プロファイルの選択・保存・削除・JSON入出力（PROJECT.md §3-6）。エラーは Error イベントで画面内の赤文字に出す。</summary>
public partial class ProfileBar : UserControl
{
    private Settings? _s;
    private bool _loading;

    /// <summary>プロファイルを読み込んだ・保存した（画面の設定を読み直す）。</summary>
    public event Action? Applied;

    /// <summary>保存・削除をした（設定の値は変わっていないので、画面や出力は作り直さない）。</summary>
    public event Action? Stored;

    /// <summary>何かの操作を始めた（前のエラーを消す）。</summary>
    public event Action? Acting;

    /// <summary>エラー（赤文字で出す）。</summary>
    public event Action<string>? Error;

    /// <summary>保存・書き出しの直前に呼ばれる（入力途中の欄を設定へ反映する）。</summary>
    public Action? Prepare { get; set; }

    public ProfileBar() => InitializeComponent();

    public void Init(Settings settings)
    {
        _s = settings;
        Reload(null);
    }

    /// <summary>一覧を読み直す。select があればそれを選ぶ。</summary>
    public void Reload(string? select)
    {
        if (_s == null) return;
        var was = _loading;
        _loading = true;
        var names = _s.Profiles.Select(p => p.Name).ToList();
        ProfileBox.ItemsSource = names;
        var cur = select ?? _s.CurrentProfile;
        if (names.Contains(cur)) ProfileBox.SelectedItem = cur; else ProfileBox.Text = select ?? "";
        _loading = was;
    }

    private void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || ProfileBox.SelectedItem is not string name) return;
        var p = _s.Profiles.FirstOrDefault(x => x.Name == name);
        if (p == null) return;
        Acting?.Invoke();
        _s.ApplyProfile(p);
        Applied?.Invoke();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        var name = ProfileBox.Text.Trim();
        if (name.Length == 0) { Error?.Invoke("プロファイル名を入力してください。"); return; }
        Prepare?.Invoke();
        _s.SaveProfile(_s.CaptureProfile(name));
        if (_s.SaveError != null) { Error?.Invoke("プロファイルを保存できませんでした: " + _s.SaveError); return; }
        _s.CurrentProfile = name;
        Reload(name);
        Stored?.Invoke();
    }

    /// <summary>選んでいる（名前欄にある）プロファイルを、もう一度読み込む。保存していない変更を捨てて元に戻したいときに使う。</summary>
    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        var name = ProfileBox.Text.Trim();
        var p = _s.Profiles.FirstOrDefault(x => x.Name == name);
        if (p == null) { Error?.Invoke("その名前のプロファイルはありません。"); return; }
        _s.ApplyProfile(p);
        Applied?.Invoke();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        var name = ProfileBox.Text.Trim();
        if (!_s.Profiles.Any(p => p.Name == name)) { Error?.Invoke("その名前のプロファイルはありません。"); return; }
        var answer = MessageBox.Show(Window.GetWindow(this), $"プロファイル「{name}」を削除しますか？",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _s.DeleteProfile(name);
        if (_s.CurrentProfile == name) _s.CurrentProfile = "";
        Reload("");
        Stored?.Invoke();
    }

    /// <summary>プルダウンを開くとき、フォルダに入れた・消したファイルを反映する。</summary>
    private void Profile_DropDownOpened(object? sender, EventArgs e)
    {
        if (_s == null) return;
        var text = ProfileBox.Text;
        Reload(text);
        ProfileBox.Text = text;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        try
        {
            Directory.CreateDirectory(_s.ProfileFolder);
            Process.Start(new ProcessStartInfo(_s.ProfileFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error?.Invoke("フォルダを開けませんでした: " + ex.Message);
        }
    }
}
