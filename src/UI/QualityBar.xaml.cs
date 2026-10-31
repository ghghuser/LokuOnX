using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>コンボボックスの選択肢（値と表示名）。</summary>
public sealed record Choice(int Value, string Label);

/// <summary>音質（ビットレート・サンプリング周波数・チャンネル）。メジャーな設定なので折りたたみの外に常時表示する。値は Settings に即保存。</summary>
public partial class QualityBar : UserControl
{
    private static readonly Choice[] RateChoices =
    {
        new(0, "自動（デバイスに合わせる）"), new(22050, "22.05 kHz"), new(32000, "32 kHz"),
        new(44100, "44.1 kHz"), new(48000, "48 kHz"), new(88200, "88.2 kHz"), new(96000, "96 kHz"),
    };

    private static readonly Choice[] ChannelChoices = { new(0, "自動"), new(1, "モノ"), new(2, "ステレオ") };

    private Settings? _s;
    private bool _loading;

    /// <summary>設定が変わった（ファイル名の例の更新用）。</summary>
    public event Action? Changed;

    public QualityBar() => InitializeComponent();

    public void Init(Settings settings)
    {
        _s = settings;
        _loading = true;
        BitrateBox.ItemsSource = EncodeOptions.Bitrates.Select(b => new Choice(b, $"{b} kbps")).ToList();
        RateBox.ItemsSource = RateChoices;
        ChannelBox.ItemsSource = ChannelChoices;
        _loading = false;
        Reload();
    }

    public void Reload()
    {
        if (_s == null) return;
        var was = _loading;
        _loading = true;
        BitrateBox.SelectedValue = _s.BitrateKbps;
        RateBox.SelectedValue = _s.SampleRate;
        ChannelBox.SelectedValue = _s.Channels;
        _loading = was;
        Refresh();
    }

    /// <summary>形式が変わったときに、ビットレートの有効/無効を更新する。</summary>
    public void Refresh()
    {
        if (_s != null) BitrateBox.IsEnabled = _s.Encode.Fmt.Lossy;
    }

    private void Bitrate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || BitrateBox.SelectedValue is not int v) return;
        _s.BitrateKbps = v;
        Changed?.Invoke();
    }

    private void Rate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || RateBox.SelectedValue is not int v) return;
        _s.SampleRate = v;
        Changed?.Invoke();
    }

    private void Channel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || ChannelBox.SelectedValue is not int v) return;
        _s.Channels = v;
        Changed?.Invoke();
    }
}
