namespace Recorder.Core;

/// <summary>
/// 出力デバイスの束ね役（M11-3。最大4台）。出力ごとに OutputEngine を1つ持ち、同じ入力（InputHub）を共有する。
/// 「すべて停止／すべて再開」（🔊のメニュー・トレイ）もここ。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class OutputHub : IDisposable
{
    private readonly InputHub _hub;
    private readonly IOutputDeviceProvider _outs;
    private readonly IAudioDeviceProvider _ins;
    private readonly object _lock = new();
    private volatile OutputEngine[] _engines = Array.Empty<OutputEngine>();
    private IReadOnlyList<InputSetting> _inputs = Array.Empty<InputSetting>();
    private HashSet<OutputEngine> _resume = new();

    public OutputHub(InputHub hub, IOutputDeviceProvider outs, IAudioDeviceProvider ins)
    {
        _hub = hub;
        _outs = outs;
        _ins = ins;
    }

    public int Count => _engines.Length;

    public OutputEngine this[int index] => _engines[index];

    public IReadOnlyList<OutputEngine> Engines => _engines;

    /// <summary>1台増やす（最大 OutputConfig.MaxOutputs）。増やせなければ null。</summary>
    public OutputEngine? Add()
    {
        lock (_lock)
        {
            if (_engines.Length >= OutputConfig.MaxOutputs) return null;
            var e = new OutputEngine(_hub, _outs, _ins);
            e.SetInputs(_inputs);
            _engines = _engines.Append(e).ToArray();
            return e;
        }
    }

    /// <summary>index 番目を止めて取り除く（デバイスを閉じるので別スレッドで片付ける）。</summary>
    public void RemoveAt(int index)
    {
        OutputEngine e;
        lock (_lock)
        {
            if (index < 0 || index >= _engines.Length) return;
            e = _engines[index];
            _engines = _engines.Where((_, i) => i != index).ToArray();
            _resume.Remove(e);
        }
        Task.Run(e.Shutdown);
    }

    /// <summary>台数が n になるまで増減する（末尾から取り除く）。</summary>
    public void EnsureCount(int n)
    {
        n = Math.Clamp(n, 0, OutputConfig.MaxOutputs);
        while (Count < n) Add();
        while (Count > n) RemoveAt(Count - 1);
    }

    /// <summary>送る入力（メイン画面で選ばれている入力）を全出力へ渡す。</summary>
    public void SetInputs(IReadOnlyList<InputSetting> inputs)
    {
        lock (_lock) _inputs = inputs;
        foreach (var e in _engines) e.SetInputs(inputs);
    }

    /// <summary>1台でも「出力中」か。</summary>
    public bool AnyRunning => _engines.Any(e => e.Running);

    /// <summary>1台でも「出力する」がオンか（つなぎ直し待ちを含む）。</summary>
    public bool AnyOn => _engines.Any(e => e.On);

    /// <summary>「すべて再開」できるか（直前の「すべて停止」でオンだった出力があるか）。</summary>
    public bool CanResume
    {
        get { lock (_lock) return _resume.Count > 0; }
    }

    /// <summary>すべての出力をオフにする。remember＝true なら、オンだった出力を覚えて「すべて再開」で戻せる。</summary>
    public void StopAll(bool remember = true)
    {
        var on = _engines.Where(e => e.On).ToList();
        lock (_lock) _resume = remember ? new HashSet<OutputEngine>(on) : new HashSet<OutputEngine>();
        foreach (var e in _engines) e.On = false;
    }

    /// <summary>すべての出力をオンにする（出力先が未選択・音が回る出力はオンのまま赤文字の理由が出る）。</summary>
    public void StartAll()
    {
        lock (_lock) _resume = new HashSet<OutputEngine>();
        foreach (var e in _engines) e.On = true;
    }

    /// <summary>1台でも「出力する」がオフか。</summary>
    public bool AnyOff => _engines.Any(e => !e.On);

    /// <summary>直前の「すべて停止」でオンだった出力をオンに戻す。</summary>
    public void ResumeAll()
    {
        List<OutputEngine> list;
        lock (_lock)
        {
            list = _resume.ToList();
            _resume = new HashSet<OutputEngine>();
        }
        foreach (var e in list) if (_engines.Contains(e)) e.On = true;
    }

    /// <summary>止めて片付ける。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Shutdown()
    {
        OutputEngine[] all;
        lock (_lock)
        {
            all = _engines;
            _engines = Array.Empty<OutputEngine>();
        }
        foreach (var e in all)
        {
            try { e.Shutdown(); } catch (Exception) { }
        }
    }

    public void Dispose() => Shutdown();
}
