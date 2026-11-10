using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 出力ウィンドウ（M11-3。出力カードを最大4枚並べる）。🔊ボタンから開く。大きさと位置を覚える。閉じても出力は止まらない。
/// 設定の読み書きと出力エンジンの操作は host（MainWindow）経由。
/// </summary>
public partial class OutputWindow : Window
{
    private readonly IOutputHost _host;
    private readonly Settings _s;
    private readonly IOutputDeviceProvider _outs;
    private readonly List<OutputCard> _cards = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private List<OutputDeviceInfo>? _devices;
    private int _tick;

    public OutputWindow(IOutputHost host, Settings settings, IOutputDeviceProvider outs)
    {
        InitializeComponent();
        _host = host;
        _s = settings;
        _outs = outs;
        RestoreRect();
        ThemeManager.Attach(this, settings.Theme);
        Reload();
        _timer.Tick += (_, _) =>
        {
            bool slow = ++_tick % 5 == 0;   // 0.25秒ごと
            foreach (var c in _cards) c.Tick(slow);
        };
        _timer.Start();
        Loaded += async (_, _) => await LoadDevicesAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            SaveRect();
        };
    }

    /// <summary>設定（出力カードの枚数・内容）が変わったとき（プリセットの読み込みなど）に、カードを作り直す。</summary>
    public void Reload()
    {
        Cards.Children.Clear();
        _cards.Clear();
        var template = (DataTemplate)Resources["OutItem"];
        for (int i = 0; i < _host.Configs.Count; i++)
        {
            var card = new OutputCard(_host, i, template, OnRemove);
            if (_devices != null) card.SetDevices(_devices);
            _cards.Add(card);
            Cards.Children.Add(card);
        }
        AddButton.Visibility = _host.Configs.Count < OutputConfig.MaxOutputs ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>メイン画面の入力（行・名前）が変わったとき。</summary>
    public void RefreshInputs()
    {
        foreach (var c in _cards) c.RefreshInputs();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_host.AddOutput()) Reload();
    }

    private void OnRemove(int index)
    {
        _host.RemoveOutput(index);
        Reload();
    }

    /// <summary>再生デバイスの一覧は別スレッドで取る（取得に時間がかかっても画面を止めない）。</summary>
    private async Task LoadDevicesAsync()
    {
        try { _devices = await Task.Run(() => _outs.List().ToList()); }
        catch (Exception) { _devices = new List<OutputDeviceInfo>(); }
        foreach (var c in _cards) c.SetDevices(_devices);
    }

    // ---- 位置と大きさ ----
    private void RestoreRect()
    {
        var parts = _s.OutputWindowRect.Split(',');
        if (parts.Length != 4) return;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return;
        // 画面の外に出てしまわないときだけ復元する
        double sl = SystemParameters.VirtualScreenLeft, st = SystemParameters.VirtualScreenTop;
        double sw = SystemParameters.VirtualScreenWidth, sh = SystemParameters.VirtualScreenHeight;
        if (v[2] < MinWidth || v[3] < MinHeight || v[0] + 80 > sl + sw || v[0] + v[2] - 80 < sl || v[1] + 40 > st + sh || v[1] < st - 10) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = v[0]; Top = v[1]; Width = v[2]; Height = v[3];
    }

    private void SaveRect()
    {
        if (WindowState != WindowState.Normal) return;
        _s.OutputWindowRect = string.Join(",", new[] { Left, Top, Width, Height }.Select(d => Math.Round(d).ToString(CultureInfo.InvariantCulture)));
    }
}
