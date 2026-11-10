using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>
/// メイン画面のうち、出力デバイス（M11-2・11-3）の配線。⇶ボタン・出力ウィンドウ・入力の持ち主（InputHub）・出力（OutputHub。最大4台）。
/// 録音・待機・入力チェック・出力は、同じ入力を InputHub から借りて共有する（録音の開始・終了で入力を開き直さないので出力が途切れない）。
/// 出力カードの設定は _outCfgs（メモリ）が正で、設定ファイルへは少し遅れて保存する。「出力する」は保存しない。
/// </summary>
public partial class MainWindow : IOutputHost
{
    private InputHub _hub = null!;
    private OutputHub _outHub = null!;
    private IOutputDeviceProvider _outProvider = null!;
    private OutputWindow? _outWindow;
    private List<OutputConfig> _outCfgs = new();
    private readonly DispatcherTimer _outTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _outSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _outMenuTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    OutputHub IOutputHost.Hub => _outHub;

    IReadOnlyList<OutputConfig> IOutputHost.Configs => _outCfgs;

    /// <summary>コンストラクターの早い段階（_settings・_provider が決まった直後）で呼ぶ。</summary>
    private void InitOutput()
    {
        _hub = new InputHub(_provider, () => _settings.AutoResume)
        {
            LabelOf = key => _devices.FirstOrDefault(d => d.Key == key)?.Label ?? key,
        };
        _outProvider = new WasapiOutputProvider();
        _outHub = new OutputHub(_hub, _outProvider, _provider);
        _outCfgs = _settings.Outputs.ToList();
        _outHub.EnsureCount(_outCfgs.Count);
        for (int i = 0; i < _outCfgs.Count; i++) ApplyOutput(i);
        _outTimer.Tick += (_, _) => UpdateOutputButton();
        _outTimer.Start();
        _outSaveTimer.Tick += (_, _) => { _outSaveTimer.Stop(); _settings.Outputs = _outCfgs; };
        _outMenuTimer.Tick += (_, _) => { _outMenuTimer.Stop(); ShowOutputMenu(); };
        OutputButton.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount < 2) return;
            _outMenuTimer.Stop();   // ダブルクリックは、メニューを出さずに出力ウィンドウを開く
            OpenOutputWindow();
            e.Handled = true;
        };
    }

    /// <summary>メイン画面で選ばれている入力を出力へ渡す（入力・出力先が変わると、出力が開き直す）。</summary>
    private void SyncOutputInputs()
    {
        _outHub.SetInputs(Devices.Inputs);
        for (int i = 0; i < _outCfgs.Count && i < _outHub.Count; i++) ApplySends(i);
        _outWindow?.RefreshInputs();
    }

    /// <summary>入力のゲインを変えた（index は選ばれている入力の順）。録音・待機・出力のどれが開いていても効く。</summary>
    private void OnInputGain(int index, double db)
    {
        var inputs = Devices.Inputs;
        if (index >= 0 && index < inputs.Count) _hub.SetGain(inputs[index].Key, db);
    }

    // ---- 設定 → 出力エンジン ----
    private void ApplySends(int i)
    {
        var cfg = _outCfgs[i];
        _outHub[i].SetSends(Devices.ActiveInputs.Select(a => cfg.SendAt(a.Slot)).ToList());
    }

    private void ApplyOutput(int i)
    {
        var cfg = _outCfgs[i];
        var e = _outHub[i];
        e.DeviceId = cfg.DeviceId;
        e.VolumeDb = cfg.VolumeDb;
        ApplySends(i);
    }

    /// <summary>プリセットなどで設定が丸ごと変わったとき。動いている出力は止めてから（E: プリセット適用時は出力を止める）作り直す。</summary>
    private void ReloadOutputsFromSettings()
    {
        _outHub.StopAll(remember: false);
        _outCfgs = _settings.Outputs.ToList();
        _outHub.EnsureCount(_outCfgs.Count);
        for (int i = 0; i < _outCfgs.Count; i++) ApplyOutput(i);
        _outWindow?.Reload();
    }

    /// <summary>遅れて保存する予定の出力設定を、いま設定へ書く。</summary>
    private void FlushOutputs()
    {
        if (!_outSaveTimer.IsEnabled) return;
        _outSaveTimer.Stop();
        _settings.Outputs = _outCfgs;
    }

    // ---- IOutputHost ----
    IReadOnlyList<(int Slot, string Name)> IOutputHost.InputSlots() => Devices.ActiveInputs;

    void IOutputHost.Update(int index, OutputConfig config)
    {
        if (index < 0 || index >= _outCfgs.Count) return;
        _outCfgs[index] = config.Normalize(index);
        ApplyOutput(index);
        _outSaveTimer.Stop();
        _outSaveTimer.Start();
    }

    bool IOutputHost.AddOutput()
    {
        if (_outCfgs.Count >= OutputConfig.MaxOutputs) return false;
        _outCfgs.Add(OutputConfig.Default(_outCfgs.Count));
        _outHub.EnsureCount(_outCfgs.Count);
        ApplyOutput(_outCfgs.Count - 1);
        _settings.Outputs = _outCfgs;
        return true;
    }

    void IOutputHost.RemoveOutput(int index)
    {
        if (index < 0 || index >= _outCfgs.Count || _outCfgs.Count <= 1) return;   // 最後の1枚は残す
        _outCfgs.RemoveAt(index);
        _outHub.RemoveAt(index);
        _settings.Outputs = _outCfgs;
    }

    bool IOutputHost.Folded(int index) => _settings.OutputFolded(index);

    void IOutputHost.SetFolded(int index, bool folded) => _settings.SetOutputFolded(index, folded);

    // ---- ⇶ボタン ----
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        // 1回クリック＝メニュー（ダブルクリックと区別するため0.3秒待つ）。ダブルクリック＝出力ウィンドウ
        _outMenuTimer.Stop();
        _outMenuTimer.Start();
    }

    private void ShowOutputMenu()
    {
        var menu = new ContextMenu { PlacementTarget = OutputButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        for (int i = 0; i < _outHub.Count && i < _outCfgs.Count; i++)
        {
            var engine = _outHub[i];
            var item = new MenuItem { Header = _outCfgs[i].Name, IsCheckable = true, IsChecked = engine.On, StaysOpenOnClick = false };
            if (engine.On && engine.Error.Length > 0) item.ToolTip = engine.Error;
            item.Click += (_, _) => engine.On = item.IsChecked;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var stop = new MenuItem { Header = "すべて停止", IsEnabled = _outHub.AnyOn };
        stop.Click += (_, _) => _outHub.StopAll();
        var resume = new MenuItem { Header = "すべて再開", IsEnabled = _outHub.CanResume };
        resume.Click += (_, _) => _outHub.ResumeAll();
        var open = new MenuItem { Header = "出力ウィンドウを開く" };
        open.Click += (_, _) => OpenOutputWindow();
        menu.Items.Add(stop);
        menu.Items.Add(resume);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.IsOpen = true;
    }

    private void OpenOutputWindow()
    {
        if (_outWindow != null)
        {
            if (_outWindow.WindowState == WindowState.Minimized) _outWindow.WindowState = WindowState.Normal;
            _outWindow.Activate();
            return;
        }
        var w = new OutputWindow(this, _settings, _outProvider) { Owner = this };
        w.Closed += (_, _) => _outWindow = null;
        _outWindow = w;
        w.Show();
    }

    private bool _outShown;

    /// <summary>トレイのツールチップの末尾に付ける出力の表示（出力中のときだけ）。</summary>
    private string OutputTip() => _outHub != null && _outHub.AnyRunning ? "　🔊 出力中" : "";

    /// <summary>0.5秒ごと。出力中は⇶の色を変え、「出力中」と出す（ウィンドウを閉じていても分かるように）。</summary>
    private void UpdateOutputButton()
    {
        bool running = _outHub.AnyRunning;
        if (running)
        {
            OutputButton.SetResourceReference(Control.ForegroundProperty, "Pastel");
            var names = _outHub.Engines.Select((e, i) => (e, i)).Where(x => x.e.Running && x.i < _outCfgs.Count).Select(x => _outCfgs[x.i].Name);
            OutputButton.ToolTip = "出力中: " + string.Join("、", names) + "（クリックでメニュー、ダブルクリックで出力ウィンドウ）";
        }
        else
        {
            OutputButton.ClearValue(Control.ForegroundProperty);
            var err = _outHub.Engines.Select(e => e.Error).FirstOrDefault(m => m.Length > 0) ?? "";
            OutputButton.ToolTip = err.Length > 0
                ? "出力: " + err
                : "出力（クリックでメニュー、ダブルクリックで出力ウィンドウ）";
        }
        if (running != _outShown)
        {
            _outShown = running;
            OutputButton.Content = running ? "⇶ 出力中" : "⇶";
        }
    }

    /// <summary>終了時。出力を止め、開いている入力をすべて閉じる（デバイスを閉じるので別スレッドで）。</summary>
    private void ShutdownOutput()
    {
        _outTimer.Stop();
        _outMenuTimer.Stop();
        if (_outSaveTimer.IsEnabled) { _outSaveTimer.Stop(); _settings.Outputs = _outCfgs; }
        try { _outWindow?.Close(); } catch (Exception) { }
        try { Task.Run(() => { _outHub.Shutdown(); _hub.Dispose(); }).Wait(3000); } catch (Exception) { }
    }
}
