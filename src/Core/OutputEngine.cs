namespace Recorder.Core;

/// <summary>
/// 出力1台の運転係（M11-2。複数台は M11-3 で OutputHub にまとめる）。
/// 「出力する」がONで出力先と入力がそろっていれば、入力を InputHub から借りて（録音と同じ入力を共有・開き直さない）、
/// 複製先（InputTap）を取り付け、再生デバイスを開いて OutputRenderer で音を送る。0.5秒ごとに状態を見直し、
/// 入力・出力先の変更に追従し、デバイスが外れる・開けないときは5秒ごとに自動で再試行する。
/// 出力側の失敗で録音は止まらない（録音の経路には触れない）。デバイスを開くので、UIスレッドを止めないよう処理は別スレッドで行う。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class OutputEngine : IDisposable
{
    private const int RetrySeconds = 5;

    private readonly InputHub _hub;
    private readonly IOutputDeviceProvider _outs;
    private readonly IAudioDeviceProvider _ins;
    private readonly object _work = new();          // 開く・閉じる処理の排他
    private readonly System.Threading.Timer _timer;
    private volatile bool _on;
    private volatile string _deviceId = "";
    private volatile IReadOnlyList<InputSetting> _inputs = Array.Empty<InputSetting>();
    private volatile IReadOnlyList<OutSend> _sends = Array.Empty<OutSend>();
    private volatile string[] _runKeys = Array.Empty<string>();   // 運転中の入力（複製先の順）
    private double _volumeDb;
    private volatile string _state = "停止中";
    private volatile string _error = "";
    private volatile OutputRenderer? _renderer;
    private bool _disposed;

    // 運転中のもの（_work の中だけで触る）
    private IOutputDevice? _dev;
    private InputHub.Entry[] _entries = Array.Empty<InputHub.Entry>();
    private InputTap[] _taps = Array.Empty<InputTap>();
    private string _runSig = "";
    private DateTime _nextTry = DateTime.MinValue;

    public OutputEngine(InputHub hub, IOutputDeviceProvider outs, IAudioDeviceProvider ins)
    {
        _hub = hub;
        _outs = outs;
        _ins = ins;
        _timer = new System.Threading.Timer(_ => Reconcile(), null, 500, 500);
    }

    /// <summary>「出力する」。ONにしても、出力先と入力がそろわなければ State / Error に理由が出る。</summary>
    public bool On
    {
        get => _on;
        set { _on = value; _nextTry = DateTime.MinValue; Kick(); }
    }

    /// <summary>出力先（"default" か デバイスID。空＝未選択）。</summary>
    public string DeviceId
    {
        get => _deviceId;
        set { _deviceId = value ?? ""; _nextTry = DateTime.MinValue; Kick(); }
    }

    /// <summary>この出力の全体音量（dB。最小＝ミュート）。動作中でも変えられる。</summary>
    public double VolumeDb
    {
        get => _volumeDb;
        set
        {
            _volumeDb = value;
            var r = _renderer;
            if (r != null) r.Bus.MasterGain = GainMath.DbToLinear(value);
        }
    }

    /// <summary>送る入力（メイン画面で選ばれている入力。上から順）。入力が変わると出力を開き直す。</summary>
    public void SetInputs(IReadOnlyList<InputSetting> inputs) => _inputs = inputs;

    /// <summary>入力ごとの送り（オンオフ・音量）。SetInputs の入力と同じ順。動作中でも変えられる（開き直さない）。</summary>
    public void SetSends(IReadOnlyList<OutSend> sends)
    {
        _sends = sends;
        ApplySends();
    }

    private void ApplySends()
    {
        var r = _renderer;
        var keys = _runKeys;
        if (r == null) return;
        var inputs = _inputs;
        var sends = _sends;
        for (int k = 0; k < keys.Length; k++)
        {
            int idx = -1;
            for (int i = 0; i < inputs.Count; i++) if (inputs[i].Key == keys[k]) { idx = i; break; }
            var s = idx >= 0 && idx < sends.Count ? sends[idx] : new OutSend();
            r.Bus.SetSend(k, s.On, GainMath.DbToLinear(s.Db));
        }
    }

    /// <summary>いまの状態（「停止中」「出力中」など）。</summary>
    public string State => _state;

    /// <summary>画面に赤文字で出す理由（なければ空）。</summary>
    public string Error => _error;

    public bool Running => _renderer != null;

    /// <summary>前回の読み出し以降の出力のピーク（L・R）とクリップ（メーター用）。</summary>
    public (float L, float R, bool Clipped) ReadPeaks() => _renderer?.ReadPeaks() ?? (0f, 0f, false);

    /// <summary>診断用の説明（途切れ回数など）。</summary>
    public string Describe() => _renderer?.Describe() ?? "";

    /// <summary>すぐに見直す（別スレッドで）。</summary>
    public void Kick() => Task.Run(Reconcile);

    private void Reconcile()
    {
        if (_disposed || !Monitor.TryEnter(_work)) return;
        try
        {
            if (_disposed) return;
            ReconcileCore();
        }
        catch (Exception ex)
        {
            TearDown();
            _error = "出力でエラーが起きました: " + ex.Message;
            _state = "停止中";
            _nextTry = DateTime.Now.AddSeconds(RetrySeconds);
        }
        finally
        {
            Monitor.Exit(_work);
        }
    }

    private void ReconcileCore()
    {
        var inputs = _inputs;
        var keys = inputs.Select(i => i.Key).Where(k => k.Length > 0).Distinct().ToList();
        var id = _deviceId;

        string? problem = null;
        if (_on)
        {
            if (id.Length == 0) problem = "出力先のデバイスを選んでください。";
            else if (keys.Count == 0) problem = "入力デバイスが選ばれていません（メイン画面で入力を選んでください）。";
            else if (OutputGuard.IsLoop(id, SafeDefaultRenderKey(), keys.Select(RealKey)))
                problem = OutputGuard.LoopMessage;
        }
        if (!_on || problem != null)
        {
            if (_dev != null || _renderer != null) TearDown();
            _error = problem ?? "";
            _state = "停止中";
            return;
        }

        var sig = id + "|" + string.Join("|", keys);
        if (_dev != null && sig != _runSig) TearDown();   // 入力・出力先が変わった: 開き直す
        if (_dev != null)
        {
            if (_dev.Failed)
            {
                var why = _dev.LastError;
                TearDown();
                _error = "出力デバイスが止まりました。" + (why.Length > 0 ? "（" + why + "）" : "") + $" {RetrySeconds}秒ごとにつなぎ直します。";
                _state = "再接続待ち";
                _nextTry = DateTime.Now.AddSeconds(RetrySeconds);
                return;
            }
            ApplySends();
            _state = "出力中";
            return;
        }
        if (DateTime.Now < _nextTry) return;

        _state = "開いています…";
        var err = Open(id, keys, inputs);
        if (err != null)
        {
            TearDown();
            _error = err + $" {RetrySeconds}秒ごとに自動でつなぎ直します。";
            _state = "再接続待ち";
            _nextTry = DateTime.Now.AddSeconds(RetrySeconds);
            return;
        }
        _runSig = sig;
        ApplySends();
        _error = "";
        _state = "出力中";
    }

    /// <summary>入力を借りて複製先を付け、出力デバイスを開く。失敗したら理由（片付けは呼び出し側の TearDown）。</summary>
    private string? Open(string id, List<string> keys, IReadOnlyList<InputSetting> inputs)
    {
        var acquired = new List<InputHub.Entry>();
        try
        {
            foreach (var k in keys) acquired.Add(_hub.Acquire(k, inputs.First(i => i.Key == k).GainDb));
        }
        finally
        {
            _entries = acquired.ToArray();   // 途中で例外になっても、借りた分は TearDown で返す
        }
        _runKeys = keys.ToArray();
        _taps = _entries.Select(_ => new InputTap()).ToArray();
        for (int i = 0; i < _entries.Length; i++) _entries[i].Stream.AddTap(_taps[i]);
        var taps = _taps;
        var linear = GainMath.DbToLinear(_volumeDb);
        _dev = _outs.Create(id);
        var err = _dev.Start((rate, ch) =>
        {
            var r = new OutputRenderer(taps, rate, ch);
            r.Bus.MasterGain = GainMath.DbToLinear(_volumeDb);
            return r;
        });
        if (err != null) return err;
        _renderer = _dev.Renderer;
        if (_renderer != null) _renderer.Bus.MasterGain = linear;
        return null;
    }

    /// <summary>運転を片付ける（出力デバイスを閉じ、複製先を外し、入力を手放す）。_work の中から呼ぶ。</summary>
    private void TearDown()
    {
        var dev = _dev;
        _dev = null;
        _renderer = null;
        try { dev?.Dispose(); } catch (Exception) { }
        for (int i = 0; i < _entries.Length; i++)
        {
            try { if (i < _taps.Length) _entries[i].Stream.RemoveTap(_taps[i]); } catch (Exception) { }
            try { _hub.Release(_entries[i]); } catch (Exception) { }
        }
        _entries = Array.Empty<InputHub.Entry>();
        _taps = Array.Empty<InputTap>();
        _runSig = "";
        _runKeys = Array.Empty<string>();
    }

    private string RealKey(string key)
    {
        try { return _ins.Resolve(key) ?? key; }
        catch (Exception) { return key; }
    }

    private string? SafeDefaultRenderKey()
    {
        try { return _ins.DefaultRenderKey(); }
        catch (Exception) { return null; }
    }

    /// <summary>止めて片付ける。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Shutdown()
    {
        _on = false;
        _disposed = true;
        _timer.Dispose();
        lock (_work) TearDown();
        _state = "停止中";
    }

    public void Dispose() => Shutdown();
}
