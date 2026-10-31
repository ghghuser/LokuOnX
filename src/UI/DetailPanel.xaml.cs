using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>コンボボックスの選択肢（値と表示名）。</summary>
public sealed record Choice(int Value, string Label);

/// <summary>「詳細設定」の中身: 音質・ファイル名テンプレート・番号・日付フォルダ。値は Settings に即保存する。</summary>
public partial class DetailPanel : UserControl
{
    private static readonly Choice[] RateChoices =
    {
        new(0, "自動（デバイスに合わせる）"), new(22050, "22.05 kHz"), new(32000, "32 kHz"),
        new(44100, "44.1 kHz"), new(48000, "48 kHz"), new(88200, "88.2 kHz"), new(96000, "96 kHz"),
    };

    private static readonly Choice[] ChannelChoices = { new(0, "自動"), new(1, "モノ"), new(2, "ステレオ") };

    private Settings? _s;
    private Func<string> _deviceLabel = () => "";
    private bool _loading;

    /// <summary>設定が変わった（現在設定サマリーの更新用）。</summary>
    public event Action? Changed;

    public DetailPanel() => InitializeComponent();

    public void Init(Settings settings, Func<string> deviceLabel)
    {
        _s = settings;
        _deviceLabel = deviceLabel;
        _loading = true;
        BitrateBox.ItemsSource = EncodeOptions.Bitrates.Select(b => new Choice(b, $"{b} kbps")).ToList();
        RateBox.ItemsSource = RateChoices;
        ChannelBox.ItemsSource = ChannelChoices;
        ReloadTemplates(null);
        _loading = false;
        Reload();
    }

    /// <summary>設定の値を画面へ読み直す（{No} は録音のたびに進むため）。</summary>
    public void Reload()
    {
        if (_s == null) return;
        var was = _loading;
        _loading = true;
        BitrateBox.SelectedValue = _s.BitrateKbps;
        RateBox.SelectedValue = _s.SampleRate;
        ChannelBox.SelectedValue = _s.Channels;
        PatternBox.Text = _s.Pattern;
        NoBox.Text = _s.NextNo.ToString();
        TrackBox.Text = _s.TrackStart.ToString();
        DateFolderCheck.IsChecked = _s.DateFolder;
        DateFormatBox.Text = _s.DateFolderFormat;
        DateFormatBox.IsEnabled = _s.DateFolder;
        _loading = was;
        Refresh();
    }

    /// <summary>形式やデバイスが変わったときに、ビットレートの有効/無効と例の表示を更新する。</summary>
    public void Refresh()
    {
        if (_s == null) return;
        BitrateBox.IsEnabled = _s.Encode.Fmt.Lossy;
        UpdateExample();
    }

    /// <summary>例の表示用。36：25 録音した場合の値。</summary>
    private NameContext SampleContext(DateTime now) =>
        new(now, _s!.NextNo, _s.TrackStart, TimeSpan.Zero, TimeSpan.FromSeconds(2185),
            FileNamer.CleanDevice(_deviceLabel()), RecordingPlan.PresetName, _s.Encode.Fmt.Ext);

    private void UpdateExample()
    {
        if (_s == null) return;
        var now = DateTime.Now;
        ExampleText.Text = "ファイル名の例（36：25 録音した場合）: " + FileNamer.FileName(PatternBox.Text, SampleContext(now))
                           + "\n保存先の例: " + RecordingPlan.PreviewFolder(_s, now);
    }

    // ---- 「？」ボタン：変数の説明と入力補助の別ウィンドウ ----
    private void PatternHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        var win = new VariableEditor("ファイル名パターン", PatternBox.Text, VariableHelp.Pattern,
            p => FileNamer.FileName(p, SampleContext(DateTime.Now))) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;
        PatternBox.Text = win.Result;
        CommitPattern();
    }

    private void DateFormatHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_s == null) return;
        var win = new VariableEditor("日付フォルダの名前", DateFormatBox.Text, VariableHelp.Date,
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

    // ---- 音質 ----
    private void Bitrate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || BitrateBox.SelectedValue is not int v) return;
        _s.BitrateKbps = v;
        Notify();
    }

    private void Rate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || RateBox.SelectedValue is not int v) return;
        _s.SampleRate = v;
        Notify();
    }

    private void Channel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || ChannelBox.SelectedValue is not int v) return;
        _s.Channels = v;
        Notify();
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
        if (name.Length == 0) { Warn("テンプレート名を入力してください。"); return; }
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
        if (!_s.Templates.Any(t => t.Name == name)) { Warn("その名前のテンプレートはありません。"); return; }
        var answer = MessageBox.Show(Window.GetWindow(this), $"テンプレート「{name}」を削除しますか？",
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

    // ---- 番号・日付フォルダ ----
    private static bool TryNumber(TextBox box, out int n) =>
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
