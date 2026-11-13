using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>「詳細設定」の中身: ファイル名テンプレート・番号・日付フォルダ。値は Settings に即保存する。（音質は QualityBar）</summary>
public partial class DetailPanel : UserControl
{
    private Settings? _s;
    private Func<string> _deviceLabel = () => "";
    private bool _loading;

    /// <summary>設定が変わった（常時表示のファイル名の例の更新用）。</summary>
    public event Action? Changed;

    /// <summary>タグの入力欄（左下の「タグ(メタデータ)」タブにある）。Init の前に MainWindow が渡す。</summary>
    public TagPanel Tags { get; set; } = null!;

    public DetailPanel()
    {
        InitializeComponent();
    }

    public void Init(Settings settings, Func<string> deviceLabel)
    {
        _s = settings;
        _deviceLabel = deviceLabel;
        _loading = true;
        ReloadTemplates(null);
        _loading = false;
        Tags.Init(settings);
        Reload();
    }

    /// <summary>設定の値を画面へ読み直す（{No} は録音のたびに進むため）。</summary>
    public void Reload()
    {
        if (_s == null) return;
        var was = _loading;
        _loading = true;
        PatternBox.Text = _s.Pattern;
        NoBox.Text = _s.NextNo.ToString();
        TrackBox.Text = _s.TrackStart.ToString();
        DateFolderCheck.IsChecked = _s.DateFolder;
        DateFormatBox.Text = _s.DateFolderFormat;
        DateFormatBox.IsEnabled = _s.DateFolder;
        Tags.Reload();
        ReloadTemplates(null);
        _loading = was;
        Refresh();
    }

    /// <summary>形式やデバイスが変わったときに、例の表示を更新する。</summary>
    public void Refresh()
    {
        if (_s == null) return;
        UpdateExample();
    }

    /// <summary>ファイル名と保存先の例（画面の常時表示用）。</summary>
    public string ExampleLine => ExampleText.Text;

    /// <summary>例の表示用。36：25 録音した場合の値。</summary>
    private NameContext SampleContext(DateTime now) =>
        new(now, _s!.NextNo, _s.TrackStart, TimeSpan.Zero, TimeSpan.FromSeconds(2185),
            FileNamer.CleanDevice(_deviceLabel()), _s.CurrentProfile, _s.Encode.Fmt.Ext);

    private void UpdateExample()
    {
        if (_s == null) return;
        var now = DateTime.Now;
        ExampleText.Text = Loc.Get("detail.example", FileNamer.FileName(PatternBox.Text, SampleContext(now)), RecordingPlan.PreviewFolder(_s, now));
    }

    // ---- 「？」ボタン：変数の説明と入力補助の別ウィンドウ ----
    private void PatternHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        var win = new VariableEditor(Loc.Get("detail.editor.pattern"), PatternBox.Text, VariableHelp.Pattern,
            p => FileNamer.FileName(p, SampleContext(DateTime.Now))) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;
        PatternBox.Text = win.Result;
        CommitPattern();
    }

    private void DateFormatHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        var win = new VariableEditor(Loc.Get("detail.editor.dateFolder"), DateFormatBox.Text, VariableHelp.Date,
            f => FileNamer.Folder(_s.Folder, true, f, SampleContext(DateTime.Now))) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;
        DateFormatBox.Text = win.Result;
        CommitDateFormat();
    }

    private void Notify()
    {
        UpdateExample();
        Changed?.Invoke();
    }

    // ---- ファイル名テンプレート ----
    private void ReloadTemplates(string? select)
    {
        if (_s == null) return;
        var was = _loading;
        _loading = true;
        var names = _s.Templates.Select(t => t.Name).ToList();
        TemplateBox.ItemsSource = names;
        var cur = select ?? _s.Template;
        TemplateBox.Text = cur;
        if (names.Contains(cur)) TemplateBox.SelectedItem = cur;
        _loading = was;
    }

    private void SetPatternText(string text)
    {
        var was = _loading;
        _loading = true;
        PatternBox.Text = text;
        _loading = was;
        UpdateExample();
    }

    private void Template_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || TemplateBox.SelectedItem is not string name) return;
        var t = _s.Templates.FirstOrDefault(x => x.Name == name);
        if (t == null) return;
        _s.Template = name;
        _s.Pattern = t.Pattern;
        SetPatternText(t.Pattern);
        Changed?.Invoke();
    }

    private void Pattern_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) UpdateExample();
    }

    private void Pattern_LostFocus(object sender, RoutedEventArgs e) => CommitPattern();

    /// <summary>パターン欄の内容を設定に反映する。空なら既定のパターンに戻す。</summary>
    private string CommitPattern()
    {
        if (_s == null) return PatternBox.Text;
        var p = PatternBox.Text.Trim();
        if (p.Length == 0)
        {
            p = FileNamer.DefaultPattern;
            SetPatternText(p);
        }
        if (p != _s.Pattern)
        {
            _s.Pattern = p;
            Changed?.Invoke();
        }
        return p;
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        var name = TemplateBox.Text.Trim();
        if (name.Length == 0) { Warn(Loc.Get("detail.err.noName")); return; }
        var pattern = CommitPattern();
        _s.SaveTemplate(name, pattern);
        _s.Template = name;
        ReloadTemplates(name);
        Changed?.Invoke();
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        var name = TemplateBox.Text.Trim();
        if (!_s.Templates.Any(t => t.Name == name)) { Warn(Loc.Get("detail.err.noSuchTemplate")); return; }
        var answer = MessageBox.Show(Window.GetWindow(this), Loc.Get("detail.deleteConfirm", name),
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _s.DeleteTemplate(name);
        ReloadTemplates("");
        Changed?.Invoke();
    }

    private void RestoreTemplates_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        _s.RestoreDefaultTemplates();
        ReloadTemplates(null);
        // 選んでいるテンプレートが復元されたなら、その内容を使う
        var t = _s.Templates.FirstOrDefault(x => x.Name == _s.Template);
        if (t != null)
        {
            _s.Pattern = t.Pattern;
            SetPatternText(t.Pattern);
        }
        Changed?.Invoke();
    }

    /// <summary>録音開始の直前に、入力途中の欄を設定へ反映する（フォーカスが外れる前に開始されても反映されるように）。</summary>
    public void CommitAll()
    {
        if (_s == null) return;
        CommitPattern();
        if (TryNumber(NoBox, out var n)) _s.NextNo = n;
        if (TryNumber(TrackBox, out var t)) _s.TrackStart = t;
        CommitDateFormat();
        Tags.Commit();
        NoBox.Text = _s.NextNo.ToString();
        TrackBox.Text = _s.TrackStart.ToString();
    }

    // ---- 番号・日付フォルダ ----
    private static bool TryNumber(NumberBox box, out int n) =>
        int.TryParse(box.Text.Trim(), out n) && n >= 1 && n <= 999999999;

    private void No_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        if (TryNumber(NoBox, out var n)) _s.NextNo = n;
        NoBox.Text = _s.NextNo.ToString();
        Notify();
    }

    private void Track_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        if (TryNumber(TrackBox, out var n)) _s.TrackStart = n;
        TrackBox.Text = _s.TrackStart.ToString();
        Notify();
    }

    private void DateFolder_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _s == null) return;
        _s.DateFolder = DateFolderCheck.IsChecked == true;
        DateFormatBox.IsEnabled = _s.DateFolder;
        Notify();
    }

    private void DateFormat_LostFocus(object sender, RoutedEventArgs e) => CommitDateFormat();

    private void CommitDateFormat()
    {
        if (_s == null) return;
        var text = DateFormatBox.Text.Trim();
        if (text.Length == 0) text = FileNamer.DefaultDateFormat;
        DateFormatBox.Text = text;
        _s.DateFolderFormat = text;
        Notify();
    }

    private void Warn(string message) =>
        MessageBox.Show(Window.GetWindow(this), message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
}
