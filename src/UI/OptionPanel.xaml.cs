using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// オプション欄のうち、無音検出・デバイスが外れたとき・タスクトレイ（PROJECT.md §3-8）。すべて既定OFF（トレイのアイコン表示だけ既定ON）。
/// 値は Settings へ即保存。無音検出・デバイスの設定は、次に録音を始めたときから効く。
/// </summary>
public partial class OptionPanel : System.Windows.Controls.UserControl
{
    private Settings _s = null!;
    private bool _loading;

    /// <summary>設定が変わった（トレイの表示や録音ボタンの文字を更新するため）。</summary>
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
        SilenceSplitCheck.IsChecked = _s.SilenceSplit;
        SilenceDbBox.Text = (-_s.SilenceDb).ToString();
        SilenceSecBox.Text = _s.SilenceSeconds.ToString();
        AutoStartCheck.IsChecked = _s.AutoStart;
        AutoStartDbBox.Text = (-_s.AutoStartDb).ToString();
        WaitCheck.IsChecked = _s.WaitForDevice;
        SplitReturnCheck.IsChecked = _s.SplitOnReturn;
        TrayCheck.IsChecked = _s.ShowTray;
        MinTrayCheck.IsChecked = _s.MinimizeToTray;
        MinTrayCheck.IsEnabled = _s.ShowTray;
        _loading = false;
    }

    private void Check_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.SilenceSplit = SilenceSplitCheck.IsChecked == true;
        _s.AutoStart = AutoStartCheck.IsChecked == true;
        _s.WaitForDevice = WaitCheck.IsChecked == true;
        _s.SplitOnReturn = SplitReturnCheck.IsChecked == true;
        _s.ShowTray = TrayCheck.IsChecked == true;
        _s.MinimizeToTray = MinTrayCheck.IsChecked == true;
        MinTrayCheck.IsEnabled = _s.ShowTray;   // アイコンが無いと、格納した画面を呼び戻せない
        Changed?.Invoke();
    }

    private void Number_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (TryNumber(SilenceDbBox, out var a)) _s.SilenceDb = -Math.Clamp(a, 10, 80);
        if (TryNumber(SilenceSecBox, out var b)) _s.SilenceSeconds = Math.Clamp(b, 1, 600);
        if (TryNumber(AutoStartDbBox, out var c)) _s.AutoStartDb = -Math.Clamp(c, 10, 80);
        SilenceDbBox.Text = (-_s.SilenceDb).ToString();
        SilenceSecBox.Text = _s.SilenceSeconds.ToString();
        AutoStartDbBox.Text = (-_s.AutoStartDb).ToString();
        Changed?.Invoke();
    }

    private static bool TryNumber(NumberBox box, out int n) =>
        int.TryParse(Digits.ToHalfWidth(box.Text).Trim(), out n);
}
