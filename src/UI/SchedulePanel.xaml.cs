using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>オプション欄の予約録音（PROJECT.md §3-8）。開始時刻・録音する長さ・くり返し。実行は MainWindow（MainWindow.Auto.cs）。</summary>
public partial class SchedulePanel : System.Windows.Controls.UserControl
{
    private Settings _s = null!;
    private bool _loading;

    /// <summary>予約の設定が変わった（次の予約の時刻を計算し直すため）。</summary>
    public event Action? Changed;

    public SchedulePanel() => InitializeComponent();

    public void Init(Settings settings)
    {
        _s = settings;
        Reload();
    }

    public void Reload()
    {
        _loading = true;
        OnCheck.IsChecked = _s.ScheduleOn;
        StartTimeBox.Text = ClockTime.FormatFull(_s.ScheduleTime);
        LengthBox.Text = SplitTime.Format(_s.ScheduleSeconds);
        RepeatBox.SelectedIndex = _s.ScheduleDaily ? 1 : 0;
        _loading = false;
    }

    /// <summary>「次の予約」の表示を更新する（常に表示）。next が null なら予約なし。予約録音が有効なあいだは色を付ける。</summary>
    public void ShowNext(DateTime? next)
    {
        if (next is { } t)
        {
            var len = _s.ScheduleSeconds > 0 ? $"、{SplitTime.Format(_s.ScheduleSeconds)} 録音して自動停止" : "、手動で停止するまで録音";
            NextText.Text = $"次の予約: {t:yyyy/MM/dd HH:mm:ss}{len}";
        }
        else
        {
            NextText.Text = _s.ReadOnly && _s.ScheduleOn
                ? "次の予約: なし（2つ目の起動では予約録音は動きません）"
                : "次の予約: なし（予約録音は使っていません）";
        }
        ApplyOnState(next != null);
    }

    /// <summary>予約録音が有効なとき、チェックの文字と「次の予約」を目立つ色（太字）にする。</summary>
    private void ApplyOnState(bool active)
    {
        if (active)
        {
            OnCheck.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ScheduleOnFg");
            OnCheck.FontWeight = FontWeights.SemiBold;
            NextText.SetResourceReference(TextBlock.ForegroundProperty, "ScheduleOnFg");
            NextText.FontWeight = FontWeights.SemiBold;
            NextText.Opacity = 1;
        }
        else
        {
            OnCheck.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            OnCheck.ClearValue(System.Windows.Controls.Control.FontWeightProperty);
            NextText.ClearValue(TextBlock.ForegroundProperty);
            NextText.ClearValue(TextBlock.FontWeightProperty);
            NextText.Opacity = 0.65;
        }
    }

    private void Check_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ScheduleOn = OnCheck.IsChecked == true;
        Changed?.Invoke();
    }

    private void Start_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ClockTime.TryParse(StartTimeBox.Text, out var sec)) _s.ScheduleTime = sec;
        StartTimeBox.Text = ClockTime.FormatFull(_s.ScheduleTime);
        Changed?.Invoke();
    }

    private void Length_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (SplitTime.TryParse(LengthBox.Text, out var sec)) _s.ScheduleSeconds = sec;
        LengthBox.Text = SplitTime.Format(_s.ScheduleSeconds);
        Changed?.Invoke();
    }

    private void Repeat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _s.ScheduleDaily = RepeatBox.SelectedIndex == 1;
        Changed?.Invoke();
    }
}
