using System.Windows;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// メイン画面のうち、入力ごとのエフェクト（M11-4）の配線。エフェクトの設定は _fx（メモリ。入力1〜4＝入力デバイス欄の行ごと）が正で、
/// 設定ファイルへは少し遅れて保存する。変更は InputHub.SetEffects で、開いている入力（録音・出力・インジケーター）へ即反映する。
/// </summary>
public partial class MainWindow : IEffectHost
{
    private readonly EffectSetting[] _fx = new EffectSetting[Settings.MaxInputs];
    private EffectWindow? _fxWindow;
    private readonly DispatcherTimer _fxSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>InputHub を作った直後に呼ぶ。保存されたエフェクトを読み込む。</summary>
    private void InitFx()
    {
        LoadFx();
        _fxSaveTimer.Tick += (_, _) => { _fxSaveTimer.Stop(); _settings.InputEffects = _fx; };
        Devices.FxRequested += OpenFx;
    }

    /// <summary>設定（ini・プロファイル）から _fx を読み直す（起動時と、プロファイルを読み込んだとき）。</summary>
    private void LoadFx()
    {
        var saved = _settings.InputEffects;
        for (int i = 0; i < _fx.Length; i++) _fx[i] = i < saved.Count ? saved[i] : new EffectSetting();
    }

    /// <summary>選ばれている入力（有効な行）のデバイスへ、その行のエフェクトを渡す。入力・デバイスが変わったとき、エフェクトを変えたときに呼ぶ。</summary>
    private void SyncEffects()
    {
        var inputs = Devices.Inputs;
        var slots = Devices.ActiveInputs;
        var list = new List<(string, EffectSetting)>();
        for (int i = 0; i < inputs.Count && i < slots.Count; i++)
        {
            int slot = slots[i].Slot;
            if (slot >= 0 && slot < _fx.Length) list.Add((inputs[i].Key, _fx[slot]));
        }
        _hub.SetEffects(list);
        for (int i = 0; i < _fx.Length; i++) Devices.SetFxState(i, _fx[i].HasAny, _fx[i].Bypass);
    }

    /// <summary>遅れて保存する予定のエフェクト設定を、いま設定へ書く（プロファイルに入る前・終了時）。</summary>
    private void FlushFx()
    {
        if (!_fxSaveTimer.IsEnabled) return;
        _fxSaveTimer.Stop();
        _settings.InputEffects = _fx;
    }

    private void ShutdownFx()
    {
        FlushFx();
        try { _fxWindow?.Close(); } catch (Exception) { }
    }

    /// <summary>
    /// 無音検出（無音での自動分割）に使う音量。「録音に反映」で効果が掛かっている入力があるときは、効果の前の音（Dry）の最大値
    /// （各デバイスのピーク×全体ゲイン）で判定する。効果が無ければ、従来どおり録音する音（ミックス後）のピーク。
    /// </summary>
    private float SilencePeak(float mixPeak, (float L, float R)[] devicePeaks)
    {
        bool recFx = false;
        foreach (var a in Devices.ActiveInputs)
            if (a.Slot >= 0 && a.Slot < _fx.Length && _fx[a.Slot].IsActive && _fx[a.Slot].ToRecording) recFx = true;
        if (!recFx) return mixPeak;
        float dry = 0;
        foreach (var (l, r) in devicePeaks) dry = Math.Max(dry, Math.Max(l, r));
        return dry * (float)GainMath.DbToLinear(Devices.MasterGainDb);
    }

    private void OpenFx(int slot)
    {
        if (_fxWindow != null)
        {
            _fxWindow.ShowSlot(slot);
            if (_fxWindow.WindowState == WindowState.Minimized) _fxWindow.WindowState = WindowState.Normal;
            _fxWindow.Activate();
            return;
        }
        var w = new EffectWindow(this, _settings, slot) { Owner = this };
        w.Closed += (_, _) => _fxWindow = null;
        _fxWindow = w;
        w.Show();
    }

    // ---- IEffectHost ----
    IReadOnlyList<(int Slot, string Name, bool Active)> IEffectHost.Slots() => Devices.AllSlots();

    EffectSetting IEffectHost.GetEffect(int slot) =>
        slot >= 0 && slot < _fx.Length ? _fx[slot].Clone() : new EffectSetting();

    void IEffectHost.SetEffect(int slot, EffectSetting fx)
    {
        if (slot < 0 || slot >= _fx.Length) return;
        fx.Normalize();
        _fx[slot] = fx;
        SyncEffects();
        _fxSaveTimer.Stop();
        _fxSaveTimer.Start();
    }

    bool IEffectHost.IsRecording => _session != null;

    string IEffectHost.WindowTitle => "入力のエフェクト";

    bool IEffectHost.HasRouting => true;

    string IEffectHost.WindowRect
    {
        get => _settings.EffectWindowRect;
        set => _settings.EffectWindowRect = value;
    }
}
