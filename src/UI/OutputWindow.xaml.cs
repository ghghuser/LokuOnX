using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private int _tabIndex;   // タブ式で選んでいる出力（作り直しても維持）

    public OutputWindow(IOutputHost host, Settings settings, IOutputDeviceProvider outs)
    {
        InitializeComponent();
        _host = host;
        _s = settings;
        _outs = outs;
        RestoreRect();
        Tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, Tabs) && Tabs.SelectedIndex >= 0) _tabIndex = Tabs.SelectedIndex;   // カード内のプルダウンの変更は無視
        };
        ThemeManager.Attach(this, settings.Theme);
        Reload();
        _timer.Tick += (_, _) =>
        {
            bool slow = ++_tick % 5 == 0;   // 0.25秒ごと
            foreach (var c in _cards) c.Tick(slow);
            if (slow) { UpdateRecButtons(); UpdateTabHeaders(); }
        };
        _timer.Start();
        Loaded += async (_, _) => await LoadDevicesAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            SaveRect();
        };
    }

    /// <summary>設定（出力カードの枚数・内容）が変わったとき（プロファイルの読み込みなど）に、カードを作り直す。</summary>
    public void Reload()
    {
        _cards.Clear();
        var template = (DataTemplate)Resources["OutItem"];
        for (int i = 0; i < _host.Configs.Count; i++)
        {
            var card = new OutputCard(_host, i, template, OnRemove);
            if (_devices != null) card.SetDevices(_devices);
            _cards.Add(card);
        }
        ApplyView();
        AddButton.Visibility = _host.Configs.Count < OutputConfig.MaxOutputs ? Visibility.Visible : Visibility.Collapsed;
        UpdateRecButtons();
    }

    // ---- 表示の切り替え（カード式／タブ式。M14） ----
    private void ApplyView()
    {
        foreach (var item in Tabs.Items.OfType<TabItem>()) item.Content = null;   // カードを置き場から外す（親が残ると別の置き場へ移せない）
        Tabs.Items.Clear();
        Cards.Children.Clear();
        bool tab = _s.IoView != "card";
        if (tab)
        {
            for (int i = 0; i < _cards.Count; i++)
                Tabs.Items.Add(new TabItem { Header = new TextBlock { Text = _host.Configs[i].Name }, Content = _cards[i] });
            if (Tabs.Items.Count > 0) Tabs.SelectedIndex = Math.Clamp(_tabIndex, 0, Tabs.Items.Count - 1);
        }
        else
        {
            foreach (var c in _cards) Cards.Children.Add(c);
        }
        Tabs.Visibility = tab ? Visibility.Visible : Visibility.Collapsed;
        Cards.Visibility = tab ? Visibility.Collapsed : Visibility.Visible;
        ViewTabButton.Opacity = tab ? 1.0 : 0.45;
        ViewCardButton.Opacity = tab ? 0.45 : 1.0;
    }

    /// <summary>出力の名前を変えたら、タブの見出しも合わせる。</summary>
    private void UpdateTabHeaders()
    {
        for (int i = 0; i < Tabs.Items.Count && i < _host.Configs.Count; i++)
            if (Tabs.Items[i] is TabItem { Header: TextBlock tb } && tb.Text != _host.Configs[i].Name) tb.Text = _host.Configs[i].Name;
    }

    private void ViewCard_Click(object sender, RoutedEventArgs e) { _s.IoView = "card"; ApplyView(); }

    private void ViewTab_Click(object sender, RoutedEventArgs e) { _s.IoView = "tab"; ApplyView(); }

    // ---- 出力の録音の一括操作（M13） ----
    private void UpdateRecButtons()
    {
        int n = _host.Configs.Count;
        bool anyOff = Enumerable.Range(0, n).Any(i => !_host.OutputRec(i));
        bool anySolo = Enumerable.Range(0, n).Any(i => _host.OutputRecMode(i) == 1);
        RecWithButton.Content = Loc.Get(anyOff ? "outputwindow.recwith.on" : "outputwindow.recwith.off");
        RecAllButton.Content = Loc.Get(anySolo ? "outputwindow.recall.stop" : "outputwindow.recall.start");
    }

    private void RecWith_Click(object sender, RoutedEventArgs e)
    {
        bool anyOff = Enumerable.Range(0, _host.Configs.Count).Any(i => !_host.OutputRec(i));
        _host.SetAllOutputRec(anyOff);
        UpdateRecButtons();
    }

    private void RecAll_Click(object sender, RoutedEventArgs e)
    {
        bool anySolo = Enumerable.Range(0, _host.Configs.Count).Any(i => _host.OutputRecMode(i) == 1);
        _host.SetAllOutputRecording(!anySolo);
        UpdateRecButtons();
    }

    /// <summary>index 番目の出力カードが見える位置までスクロールする（メイン画面の出力ランプの「詳細設定」から）。</summary>
    public void ShowCard(int index)
    {
        if (index < 0 || index >= _cards.Count) return;
        if (Tabs.Visibility == Visibility.Visible) Tabs.SelectedIndex = index;
        else _cards[index].BringIntoView();
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
