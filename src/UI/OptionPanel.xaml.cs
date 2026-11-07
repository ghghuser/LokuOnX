using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// オプション欄のうち、無音検出（PROJECT.md §3-8）。すべて既定OFF。
/// 値は Settings へ即保存。無音検出の設定は、次に録音を始めたときから効く。
/// タスクトレイ・ホットキー・デバイスが外れたときの設定は、右上の歯車の設定ウィンドウ（SettingsWindow）の「詳細設定」にある。
/// </summary>
public partial class OptionPanel : System.Windows.Controls.UserControl
{
    private Settings _s = null!;
    private bool _loading;

    /// <summary>設定が変わった（録音ボタンの文字などを更新するため）。</summary>
    public event Action? Changed;

    public OptionPanel() => InitializeComponent();

    public void Init(Settings settings)
    {
        _s = settings;
        Reload();
    }

    public void Reload()
    {
        _loading = true;
        AutoStartCheck.IsChecked = _s.AutoStart;
        AutoStartDbBox.Text = (-_s.AutoStartDb).ToString();
        SilenceSplitCheck.IsChecked = _s.SilenceSplit;
        SilenceDbBox.Text = (-_s.SilenceDb).ToString();
        SilenceSecBox.Text = _s.SilenceSeconds.ToString();
        _loading = false;
    }

    private void Check_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AutoStart = AutoStartCheck.IsChecked == true;
        _s.SilenceSplit = SilenceSplitCheck.IsChecked == true;
        Changed?.Invoke();
    }

    private void Number_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (TryNumber(AutoStartDbBox, out var c)) _s.AutoStartDb = -Math.Clamp(c, 10, 80);
        if (TryNumber(SilenceDbBox, out var a)) _s.SilenceDb = -Math.Clamp(a, 10, 80);
        if (TryNumber(SilenceSecBox, out var b)) _s.SilenceSeconds = Math.Clamp(b, 1, 600);
        AutoStartDbBox.Text = (-_s.AutoStartDb).ToString();
        SilenceDbBox.Text = (-_s.SilenceDb).ToString();
        SilenceSecBox.Text = _s.SilenceSeconds.ToString();
        Changed?.Invoke();
    }

    private static bool TryNumber(NumberBox box, out int n) =>
        int.TryParse(Digits.ToHalfWidth(box.Text).Trim(), out n);
}
