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
    private EffectWindow? _outFxWindow;   // 出力のエフェクトのウィンドウ（1つだけ。出力カードはタブ）
    private List<OutputConfig> _outCfgs = new();
    private readonly DispatcherTimer _outTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _outSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    OutputHub IOutputHost.Hub => _outHub;

    IReadOnlyList<OutputConfig> IOutputHost.Configs => _outCfgs;

    /// <summary>コンストラクターの早い段階（_settings・_provider が決まった直後）で呼ぶ。</summary>
    private void InitOutput()
    {
        _hub = new InputHub(_provider, () => _settings.AutoResume)
        {
            LabelOf = key => _devices.FirstOrDefault(d => d.Key == key)?.Label ?? key,
        };
        InitFx();   // 入力ごとのエフェクト（M11-4）。保存された設定を読み込み、入力を開くときに渡す
        _outProvider = new WasapiOutputProvider();
        _outHub = new OutputHub(_hub, _outProvider, _provider);
        _outCfgs = _settings.Outputs.ToList();
        _outHub.EnsureCount(_outCfgs.Count);
        for (int i = 0; i < _outCfgs.Count; i++) ApplyOutput(i);
        _outTimer.Tick += (_, _) => UpdateOutputButton();
        _outTimer.Start();
        _outSaveTimer.Tick += (_, _) => { _outSaveTimer.Stop(); _settings.Outputs = _outCfgs; };
    }

    /// <summary>メイン画面で選ばれている入力を出力へ渡す（入力・出力先が変わると、出力が開き直す）。</summary>
    private void SyncOutputInputs()
    {
        SyncEffects();   // 入力（行）が変わったので、エフェクトも渡し直す（出力・録音が入力を開く前に）
        _outHub.SetInputs(Devices.Inputs);
        for (int i = 0; i < _outCfgs.Count && i < _outHub.Count; i++) ApplySends(i);
        _outWindow?.RefreshInputs();
        _fxWindow?.RefreshSlots();
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
        var fx = cfg.FxCopy();
        if (!e.Fx.SameSound(fx)) e.Fx = fx;   // 音量などを動かすたびに、エフェクトの状態を作り直さない
        ApplySends(i);
    }

    /// <summary>プロファイルなどで設定が丸ごと変わったとき。動いている出力は止めてから（E: プロファイル適用時は出力を止める）作り直す。</summary>
    private void ReloadOutputsFromSettings()
    {
        _outHub.StopAll(remember: false);
        _outCfgs = _settings.Outputs.ToList();
        _outHub.EnsureCount(_outCfgs.Count);
        for (int i = 0; i < _outCfgs.Count; i++) ApplyOutput(i);
        _outWindow?.Reload();
        _outFxWindow?.Reload();
        Devices.RefreshWarnings();
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
        bool destChanged = _outCfgs[index].DeviceId != config.DeviceId;
        _outCfgs[index] = config.Normalize(index);
        if (destChanged) Devices.RefreshWarnings();
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
        Devices.RefreshWarnings();
        return true;
    }

    void IOutputHost.RemoveOutput(int index)
    {
        if (index < 0 || index >= _outCfgs.Count || _outCfgs.Count <= 1) return;   // 最後の1枚は残す
        _outCfgs.RemoveAt(index);
        _outHub.RemoveAt(index);
        _settings.Outputs = _outCfgs;
        _outFxWindow?.Reload();   // タブが減る
        Devices.RefreshWarnings();
    }

    void IOutputHost.OpenOutputFx(int index, Window owner)
    {
        if (index < 0 || index >= _outCfgs.Count) return;
        if (_outFxWindow != null)
        {
            _outFxWindow.ShowSlot(index);
            if (_outFxWindow.WindowState == WindowState.Minimized) _outFxWindow.WindowState = WindowState.Normal;
            _outFxWindow.Activate();
            return;
        }
        var w = new EffectWindow(new OutputFxHost(this, _settings), _settings, index) { Owner = owner ?? this };
        w.Closed += (_, _) => _outFxWindow = null;
        _outFxWindow = w;
        w.Show();
    }

    private void CloseOutputFxWindows()
    {
        try { _outFxWindow?.Close(); } catch (Exception) { }
        _outFxWindow = null;
    }

    void IOutputHost.RenameInput(int slot, string name) => Devices.SetInputName(slot, name);

    /// <summary>この出力先が、いま選んでいる入力の「🔊 ○○」（PC内部音）と同じデバイスか（音が回るので出力できない組み合わせ）。</summary>
    bool IOutputHost.IsLoopOutput(string id)
    {
        if (id.Length == 0) return false;
        string? defaultRender;
        try { defaultRender = _provider.DefaultRenderKey(); }
        catch (Exception) { defaultRender = null; }
        return OutputGuard.IsLoop(id, defaultRender, Devices.Inputs.Select(i => RealInputKey(i.Key)));
    }

    /// <summary>この入力（Key）を使うと、音が回るため出力できなくなる出力カードの名前（出力先が未選択のカードは対象外）。</summary>
    private IReadOnlyList<string> LoopOutputNames(string inputKey)
    {
        var real = RealInputKey(inputKey);
        if (!real.StartsWith("render:", StringComparison.Ordinal)) return Array.Empty<string>();
        string? defaultRender;
        try { defaultRender = _provider.DefaultRenderKey(); }
        catch (Exception) { defaultRender = null; }
        var list = new List<string>();
        foreach (var c in _outCfgs)
            if (c.DeviceId.Length > 0 && OutputGuard.IsLoop(c.DeviceId, defaultRender, new[] { real })) list.Add(c.Name);
        return list;
    }

    string IOutputHost.RealOutputId(string id)
    {
        if (id != OutputDeviceInfo.DefaultId) return id;
        try
        {
            var key = _provider.DefaultRenderKey();
            return key != null && key.StartsWith("render:", StringComparison.Ordinal) ? key.Substring(7) : id;
        }
        catch (Exception) { return id; }
    }

    bool IOutputHost.Folded(int index) => _settings.OutputFolded(index);

    void IOutputHost.SetFolded(int index, bool folded) => _settings.SetOutputFolded(index, folded);

    // ---- ⇶ボタン ----
    /// <summary>左クリック＝出力ウィンドウ（待たずにすぐ開く）。右クリック＝メニュー（出力ごとのオン/オフ・すべて停止・すべて再開）。</summary>
    private void Output_Click(object sender, RoutedEventArgs e) => OpenOutputWindow();

    private void Output_RightClick(object sender, MouseButtonEventArgs e)
    {
        ShowOutputMenu();
        e.Handled = true;
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
        var start = new MenuItem { Header = Loc.Get("mainwindow.output.s01"), IsEnabled = _outHub.AnyOff };
        start.Click += (_, _) => _outHub.StartAll();
        var stop = new MenuItem { Header = Loc.Get("mainwindow.output.s02"), IsEnabled = _outHub.AnyOn };
        stop.Click += (_, _) => _outHub.StopAll();
        var resume = new MenuItem { Header = Loc.Get("mainwindow.output.s03"), IsEnabled = _outHub.CanResume };
        resume.Click += (_, _) => _outHub.ResumeAll();
        var open = new MenuItem { Header = Loc.Get("mainwindow.output.s04") };
        open.Click += (_, _) => OpenOutputWindow();
        menu.Items.Add(start);
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

    private int _outShown;   // 出力中の枚数（0＝出力中なし。表示が変わったときだけ文字を書き換える）

    /// <summary>トレイのツールチップの末尾に付ける出力の表示（出力中のときだけ）。</summary>
    private string OutputTip() => _outHub != null && _outHub.AnyRunning ? ("　" + Loc.Get("mainwindow.output.s05")) : "";

    /// <summary>0.5秒ごと。出力中は⇶の色を変え、「出力中」と出す（ウィンドウを閉じていても分かるように）。</summary>
    private void UpdateOutputButton()
    {
        bool running = _outHub.AnyRunning;
        if (running)
        {
            OutputButton.SetResourceReference(Control.ForegroundProperty, "LampOn");   // 入力デバイスのランプがオンのときと同じ緑
            var names = _outHub.Engines.Select((e, i) => (e, i)).Where(x => x.e.Running && x.i < _outCfgs.Count).Select(x => _outCfgs[x.i].Name);
            OutputButton.ToolTip = Loc.Get("mainwindow.output.s06") + string.Join("、", names) + Loc.Get("mainwindow.output.s07");
        }
        else
        {
            OutputButton.ClearValue(Control.ForegroundProperty);
            var err = _outHub.Engines.Select(e => e.Error).FirstOrDefault(m => m.Length > 0) ?? "";
            OutputButton.ToolTip = err.Length > 0
                ? Loc.Get("mainwindow.output.s08") + err
                : Loc.Get("mainwindow.output.s09");
        }
        int count = running ? _outHub.Engines.Count(e => e.Running) : 0;
        int total = _outHub.Count;
        int shown = count * 10 + total;   // 枚数かカード数が変わったときだけ文字を書き換える
        if (shown != _outShown)
        {
            _outShown = shown;
            // 出力中の枚数 / カードの枚数。カードが1枚のときは数字を出さない
            OutputButton.Content = count == 0 ? "⇶" : total <= 1 ? Loc.Get("mainwindow.output.s10") : Loc.Get("mainwindow.output.s11", count, total);
        }
    }

    /// <summary>終了時。出力を止め、開いている入力をすべて閉じる（デバイスを閉じるので別スレッドで）。</summary>
    private void ShutdownOutput()
    {
        _outTimer.Stop();
        ShutdownFx();
        if (_outSaveTimer.IsEnabled) { _outSaveTimer.Stop(); _settings.Outputs = _outCfgs; }
        CloseOutputFxWindows();
        try { _outWindow?.Close(); } catch (Exception) { }
        try { Task.Run(() => { _outHub.Shutdown(); _hub.Dispose(); }).Wait(3000); } catch (Exception) { }
    }
}
