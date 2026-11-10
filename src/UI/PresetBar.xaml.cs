using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>プリセットの選択・保存・削除・JSON入出力（PROJECT.md §3-6）。エラーは Error イベントで画面内の赤文字に出す。</summary>
public partial class PresetBar : UserControl
{
    private Settings? _s;
    private bool _loading;

    /// <summary>プリセットを読み込んだ・保存した（画面の設定を読み直す）。</summary>
    public event Action? Applied;

    /// <summary>保存・削除をした（設定の値は変わっていないので、画面や出力は作り直さない）。</summary>
    public event Action? Stored;

    /// <summary>何かの操作を始めた（前のエラーを消す）。</summary>
    public event Action? Acting;

    /// <summary>エラー（赤文字で出す）。</summary>
    public event Action<string>? Error;

    /// <summary>保存・書き出しの直前に呼ばれる（入力途中の欄を設定へ反映する）。</summary>
    public Action? Prepare { get; set; }

    public PresetBar() => InitializeComponent();

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
        var names = _s.Presets.Select(p => p.Name).ToList();
        PresetBox.ItemsSource = names;
        var cur = select ?? _s.CurrentPreset;
        if (names.Contains(cur)) PresetBox.SelectedItem = cur; else PresetBox.Text = select ?? "";
        _loading = was;
    }

    private void Preset_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || PresetBox.SelectedItem is not string name) return;
        var p = _s.Presets.FirstOrDefault(x => x.Name == name);
        if (p == null) return;
        Acting?.Invoke();
        _s.ApplyPreset(p);
        Applied?.Invoke();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        var name = PresetBox.Text.Trim();
        if (name.Length == 0) { Error?.Invoke("プリセット名を入力してください。"); return; }
        Prepare?.Invoke();
        _s.SavePreset(_s.CapturePreset(name));
        if (_s.SaveError != null) { Error?.Invoke("プリセットを保存できませんでした: " + _s.SaveError); return; }
        _s.CurrentPreset = name;
        Reload(name);
        Stored?.Invoke();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        var name = PresetBox.Text.Trim();
        if (!_s.Presets.Any(p => p.Name == name)) { Error?.Invoke("その名前のプリセットはありません。"); return; }
        var answer = MessageBox.Show(Window.GetWindow(this), $"プリセット「{name}」を削除しますか？",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _s.DeletePreset(name);
        if (_s.CurrentPreset == name) _s.CurrentPreset = "";
        Reload("");
        Stored?.Invoke();
    }

    /// <summary>プルダウンを開くとき、フォルダに入れた・消したファイルを反映する。</summary>
    private void Preset_DropDownOpened(object? sender, EventArgs e)
    {
        if (_s == null) return;
        var text = PresetBox.Text;
        Reload(text);
        PresetBox.Text = text;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        try
        {
            Directory.CreateDirectory(_s.PresetFolder);
            Process.Start(new ProcessStartInfo(_s.PresetFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error?.Invoke("フォルダを開けませんでした: " + ex.Message);
        }
    }
}
