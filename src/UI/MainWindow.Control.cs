using System.Text.Json;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// メイン画面のうち、制御API（M17）の配線。設定の「外部アプリからの操作を受け付ける」がONのあいだだけ名前付きパイプで待ち受け、
/// 受けた命令は画面のスレッドで、画面のボタン・つまみと同じ経路で実行する（録音の開始・停止・一時停止・分割、エフェクトのオン／オフ、出力のオン／オフ・音量、入力ゲイン・ミュート、全体ゲイン）。
/// 状態が変わったら、購読している接続へ流す（0.25秒ごとに調べ、変わったときだけ）。2つめの起動（読み取り専用）では待ち受けない。
/// </summary>
public partial class MainWindow
{
    private ControlServer? _control;
    private readonly DispatcherTimer _controlTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _controlLast = "";

    /// <summary>設定に合わせて、待ち受けを始める／止める（起動時と、設定ウィンドウで切り替えたとき）。</summary>
    private void ApplyControlApi()
    {
        bool want = _settings.ControlApiOn && !_settings.ReadOnly;
        if (want && _control == null)
        {
            try
            {
                var bridge = new ControlBridge(this);
                _control = new ControlServer(ControlProtocol.PipeName(AppPaths.Dir, AppInfo.Name), new ControlDispatcher(bridge, AppInfo.Name, AppInfo.Version));
                _control.Start();
                _controlTimer.Tick -= ControlTick;
                _controlTimer.Tick += ControlTick;
                _controlTimer.Start();
            }
            catch (Exception ex)
            {
                _control = null;
                StatusText.Text = Loc.Get("control.err.start", ex.Message);
            }
        }
        else if (!want && _control != null)
        {
            StopControlApi();
        }
    }

    private void StopControlApi()
    {
        _controlTimer.Stop();
        try { _control?.Stop(); } catch (Exception) { }
        _control = null;
        _controlLast = "";
    }

    private void ControlTick(object? sender, EventArgs e)
    {
        var server = _control;
        if (server == null || !server.HasSubscribers) return;
        var state = BuildControlState();
        var json = JsonSerializer.Serialize(state);
        if (json == _controlLast) return;
        _controlLast = json;
        server.Publish(state);
    }

    /// <summary>いまの状態（制御APIの state／通知）。画面のスレッドで呼ぶ。入力・出力の番号は 1 から。</summary>
    private object BuildControlState()
    {
        var inputs = Devices.InputStates().Select(s => new
        {
            index = s.Slot + 1,
            active = s.Active,
            gainDb = s.Db,
            gainRDb = s.DbR,
            muted = s.Muted,
            fx = FxState(s.Slot),
        }).ToList();
        var outputs = Enumerable.Range(0, Math.Min(_outCfgs.Count, _outHub.Count)).Select(i => new
        {
            index = i + 1,
            name = _outCfgs[i].Name,
            on = _outHub[i].On,
            running = _outHub[i].Running,
            volumeDb = _outCfgs[i].VolumeDb,
            fx = new { any = _outCfgs[i].FxCopy().HasAny, on = !_outCfgs[i].FxCopy().Bypass },
        }).ToList();
        return new
        {
            recording = _session != null,
            paused = _session?.Paused ?? false,
            standby = _standby,
            track = _session?.CurrentTrackNumber,
            masterGainDb = Devices.MasterGainDb,
            inputs,
            outputs,
        };
    }

    private object FxState(int slot) =>
        slot >= 0 && slot < _fx.Length ? new { any = _fx[slot].HasAny, on = !_fx[slot].Bypass } : new { any = false, on = true };

    /// <summary>制御API → 画面。命令は画面のスレッドへ渡して実行する。null＝成功、文字列＝失敗の理由。</summary>
    private sealed class ControlBridge : IControlHost
    {
        private readonly MainWindow _w;
        public ControlBridge(MainWindow w) => _w = w;

        private Task<T> Ui<T>(Func<T> f) => _w.Dispatcher.InvokeAsync(f).Task;

        public Task<object> State() => Ui(() => _w.BuildControlState());

        public Task<string?> RecStart() => Ui(() => _w.ControlRecStart()).Unwrap();
        public Task<string?> RecStop() => Ui(() => _w.ControlRecStop()).Unwrap();
        public Task<string?> RecSplit() => Ui(() => _w.ControlRecSplit()).Unwrap();
        public Task<string?> RecPause(bool pause) => Ui(() => _w.ControlRecPause(pause));
        public Task<string?> FxOn(bool output, int index, bool on) => Ui(() => _w.ControlFxOn(output, index, on));
        public Task<string?> OutputOn(int index, bool on) => Ui(() => _w.ControlOutputOn(index, on));
        public Task<string?> OutputVolume(int index, double db) => Ui(() => _w.ControlOutputVolume(index, db));
        public Task<string?> InputGain(int index, double db) => Ui(() => _w.ControlInputGain(index, db));
        public Task<string?> InputMute(int index, bool mute) => Ui(() => _w.ControlInputMute(index, mute));
        public Task<string?> MasterGain(double db) => Ui<string?>(() => { _w.Devices.SetMasterGainDb(db); return null; });
    }

    private Task<string?> ControlRecStart()
    {
        if (_session != null) return Task.FromResult<string?>(Loc.Get("control.err.alreadyRecording"));
        if (_standby) return Task.FromResult<string?>(Loc.Get("control.err.standby"));
        if (!StartButton.IsEnabled) return Task.FromResult<string?>(Loc.Get("control.err.busy"));
        return Task.FromResult(StartRecording() ? null : Loc.Get("control.err.cannotStart"));   // 開始できなかった理由は画面の赤文字に出ている
    }

    private async Task<string?> ControlRecStop()
    {
        if (_session == null) return Loc.Get("control.err.notRecording");
        await StopAsync(null);
        return null;
    }

    private async Task<string?> ControlRecSplit()
    {
        if (_session == null) return Loc.Get("control.err.notRecording");
        if (!SplitButton.IsEnabled) return Loc.Get("control.err.cannotSplit");
        await SplitAsync();
        return null;
    }

    private string? ControlRecPause(bool pause)
    {
        if (_session == null) return Loc.Get("control.err.notRecording");
        if (_session.Paused == pause) return null;   // すでにその状態
        if (!PauseButton.IsEnabled) return Loc.Get("control.err.cannotPause");
        TogglePause();
        return null;
    }

    private string? ControlFxOn(bool output, int index, bool on)
    {
        if (!output)
        {
            var host = (IEffectHost)this;
            var fx = host.GetEffect(index);
            fx.Bypass = !on;
            host.SetEffect(index, fx);
            _fxWindow?.Reload();
            return null;
        }
        if (index >= _outCfgs.Count) return Loc.Get("control.err.noOutput", index + 1);
        var ofx = _outCfgs[index].FxCopy();
        ofx.Bypass = !on;
        ((IOutputHost)this).Update(index, _outCfgs[index] with { Fx = ofx });
        _outFxWindow?.Reload();
        _outWindow?.Reload();
        return null;
    }

    private string? ControlOutputOn(int index, bool on)
    {
        if (index >= _outCfgs.Count || index >= _outHub.Count) return Loc.Get("control.err.noOutput", index + 1);
        _outHub[index].On = on;
        return null;
    }

    private string? ControlOutputVolume(int index, double db)
    {
        if (index >= _outCfgs.Count) return Loc.Get("control.err.noOutput", index + 1);
        ((IOutputHost)this).Update(index, _outCfgs[index] with { VolumeDb = db });
        _outWindow?.Reload();
        return null;
    }

    private string? ControlInputGain(int index, double db)
    {
        if (!Devices.HasInput(index)) return Loc.Get("control.err.noInput", index + 1);
        Devices.SetInputGainDb(index, db);
        return null;
    }

    private string? ControlInputMute(int index, bool mute)
    {
        if (!Devices.HasInput(index)) return Loc.Get("control.err.noInput", index + 1);
        Devices.SetInputMute(index, mute);
        return null;
    }
}
