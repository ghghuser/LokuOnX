using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>コンボボックスの選択肢（値と表示名）。</summary>
public sealed record Choice(int Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>音質（ビットレート・サンプリング周波数・チャンネル・圧縮率）。メジャーな設定なので折りたたみの外に常時表示する。値は Settings に即保存。</summary>
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
    private string _compFormat = "";

    /// <summary>設定が変わった（ファイル名の例の更新用）。</summary>
    public event Action? Changed;

    public QualityBar() => InitializeComponent();

    public void Init(Settings settings)
    {
        _s = settings;
        _loading = true;
        FormatBox.ItemsSource = OutputFormat.All;
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
        FormatBox.SelectedValue = _s.Format;
        BitrateBox.SelectedValue = _s.BitrateKbps;
        RateBox.SelectedValue = _s.SampleRate;
        ChannelBox.SelectedValue = _s.Channels;
        _loading = was;
        Refresh();
    }

    /// <summary>形式が変わったときに、ビットレートの有効/無効と、圧縮率の欄（形式ごとの選択肢）を更新する。</summary>
    public void Refresh()
    {
        if (_s == null) return;
        var fmt = _s.Encode.Fmt;
        var spec = fmt.Compression;
        CompPanel.Visibility = spec == null ? Visibility.Collapsed : Visibility.Visible;
        if (spec != null && _compFormat != fmt.Key)
        {
            var was = _loading;
            _loading = true;
            CompLabel.Text = spec.Label;
            CompBox.ToolTip = spec.Tooltip;
            var list = new List<Choice>();
            if (spec.NoneLabel != null) list.Add(new Choice(-1, spec.NoneLabel));
            for (int v = spec.Min; v <= spec.Max; v++)
                list.Add(new Choice(v, v == spec.Default ? $"{v}（既定）" : v.ToString()));
            CompBox.ItemsSource = list;
            _compFormat = fmt.Key;
            _loading = was;
        }
        if (spec != null)
        {
            var was = _loading;
            _loading = true;
            CompBox.SelectedValue = _s.Compression;
            _loading = was;
        }
        // ogg は「Vorbis品質」を選んだらビットレートは使われない
        BitrateBox.IsEnabled = fmt.UsesBitrate && !(fmt.Key == "ogg" && _s.Compression >= 0);
    }

    private void Format_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || FormatBox.SelectedValue is not string f) return;
        _s.Format = f;
        Refresh();
        Changed?.Invoke();
    }

    private void Comp_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _s == null || CompBox.SelectedValue is not int v) return;
        _s.Compression = v;
        Refresh();
        Changed?.Invoke();
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
