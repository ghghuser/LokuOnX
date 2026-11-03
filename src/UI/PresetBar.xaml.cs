using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>プリセットの選択・保存・削除・JSON入出力（PROJECT.md §3-6）。エラーは Error イベントで画面内の赤文字に出す。</summary>
public partial class PresetBar : UserControl
{
    private Settings? _s;
    private bool _loading;

    /// <summary>プリセットを読み込んだ・保存した（画面の設定を読み直す）。</summary>
    public event Action? Applied;

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
        _s.CurrentPreset = name;
        Reload(name);
        Applied?.Invoke();
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
        Applied?.Invoke();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        Prepare?.Invoke();
        var name = PresetBox.Text.Trim();
        if (name.Length == 0) name = _s.CurrentPreset;
        var dlg = new SaveFileDialog
        {
            Filter = "プリセット (*.json)|*.json", DefaultExt = ".json",
            FileName = FileNamer.Sanitize(name) + ".json",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dlg.FileName, PresetJson.ToFile(_s.CapturePreset(name)), new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error?.Invoke("書き出せませんでした: " + ex.Message);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        Acting?.Invoke();
        var dlg = new OpenFileDialog { Filter = "プリセット (*.json)|*.json|すべてのファイル|*.*" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        string text;
        try { text = File.ReadAllText(dlg.FileName, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error?.Invoke("読み込めませんでした: " + ex.Message);
            return;
        }
        var list = PresetJson.Parse(text, out var err);
        if (list == null) { Error?.Invoke(err ?? "読み込めませんでした。"); return; }
        foreach (var p in list) _s.SavePreset(p);
        _s.ApplyPreset(list[0]);
        Reload(list[0].Name);
        Applied?.Invoke();
    }
}
