using System.Diagnostics;

namespace Recorder.Core;

/// <summary>
/// 入力デバイスの「持ち主」（M11-2。PROJECT.md §11・docs/M11-spec.txt の A2）。
/// 録音・待機中のレベル表示・出力デバイスへの送り出しは、同じ入力を共有する（参照カウント）。
/// 誰かが使っている間は開いたままにするので、録音の開始・終了で入力を開き直さない（出力が途切れない。ASIOのように専有するドライバでも共有できる）。
/// 全員が手放したら閉じる。共通の時計（Now）を持ち、録音はこの時計で時間軸を合わせる。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class InputHub : IDisposable
{
    /// <summary>開いている入力1台（デバイスの Key ごとに1つ）。</summary>
    public sealed class Entry
    {
        public string Key { get; }
        public string Label { get; }
        public DeviceInput Input { get; }
        public DeviceStream Stream => Input.Stream;
        internal int Refs;

        internal Entry(string key, string label, DeviceInput input)
        {
            Key = key;
            Label = label;
            Input = input;
        }
    }

    private readonly IAudioDeviceProvider _provider;
    private readonly Func<bool> _autoResume;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private bool _disposed;

    /// <summary>通知に使う表示名を返す（Key → 名前）。未設定なら Key をそのまま使う。</summary>
    public Func<string, string>? LabelOf { get; set; }

    /// <param name="autoResume">デバイスが外れたとき、戻ったら自動で再開するか（作るときの設定が使われる）。</param>
    public InputHub(IAudioDeviceProvider provider, Func<bool> autoResume)
    {
        _provider = provider;
        _autoResume = autoResume;
    }

    /// <summary>共通の時計（秒）。ハブを作ってからの経過時間で、止まらない。</summary>
    public double Now() => _clock.Elapsed.TotalSeconds;

    /// <summary>
    /// 入力を使い始める。すでに開いていればそれを共有し、無ければ開く（開けなくても、あとからつながれば自動で再開する）。
    /// 使い終わったら必ず Release する。開くのに時間がかかることがある（UIスレッドを長く止めたくない場合は別スレッドから）。
    /// </summary>
    public Entry Acquire(string key, double gainDb)
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputHub));
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.Refs++;
                existing.Stream.SetGainDb(gainDb);
                return existing;
            }
            var label = LabelOf?.Invoke(key) ?? key;
            var stream = new DeviceStream(48000, 2, Now, gainDb);
            var input = new DeviceInput(_provider, key, label, stream, _autoResume());
            var entry = new Entry(key, label, input) { Refs = 1 };
            _entries[key] = entry;
            input.Start();   // つながらなくても監視は始まる（IsConnected / LastError で確認する）
            return entry;
        }
    }

    /// <summary>使い終わった。誰も使っていなければ閉じる（デバイスを閉じるので、UIスレッドで直接呼ばず Task.Run 経由で）。</summary>
    public void Release(Entry entry)
    {
        bool close = false;
        lock (_lock)
        {
            if (entry.Refs <= 0) return;
            entry.Refs--;
            if (entry.Refs == 0)
            {
                if (_entries.TryGetValue(entry.Key, out var cur) && ReferenceEquals(cur, entry)) _entries.Remove(entry.Key);
                close = true;
            }
        }
        if (!close) return;
        try { entry.Input.Stop(); } catch (Exception) { }
    }

    /// <summary>開いている入力のゲイン（dB）を変える（録音中・待機中・出力中のどれでも効く）。</summary>
    public void SetGain(string key, double gainDb)
    {
        Entry? e;
        lock (_lock) _entries.TryGetValue(key, out e);
        e?.Stream.SetGainDb(gainDb);
    }

    /// <summary>いま開いている入力の数（診断・テスト用）。</summary>
    public int OpenCount
    {
        get { lock (_lock) return _entries.Count; }
    }

    public void Dispose()
    {
        List<Entry> all;
        lock (_lock)
        {
            _disposed = true;
            all = _entries.Values.ToList();
            _entries.Clear();
        }
        foreach (var e in all)
        {
            try { e.Input.Stop(); } catch (Exception) { }
        }
    }
}
