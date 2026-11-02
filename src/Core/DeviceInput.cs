namespace Recorder.Core;

/// <summary>
/// 選ばれた1台のデバイス。つなぐ・外れたら1秒ごとにつなぎ直す・「既定のデバイス」なら Windows の既定が変わったら切り替える。
/// 外れている間は DeviceStream が壁時計に合わせて無音で埋める（PROJECT.md §3-2）。WPF には依存しない。
/// </summary>
public sealed class DeviceInput : IDisposable
{
    private readonly IAudioDeviceProvider _provider;
    private readonly string _key;           // 選ばれた Key（擬似デバイス "capture:default" などもあり）
    private readonly bool _autoResume;
    private readonly object _lock = new();
    private System.Threading.Timer? _poll;
    private IAudioSource? _src;
    private string? _srcKey;                // つながっている実際のデバイスの Key
    private int _lost;                      // 1 = つながっているデバイスが止まった
    private bool _reportedLost;
    private bool _stopping;

    public DeviceStream Stream { get; }
    public string Label { get; }

    /// <summary>直近の接続エラー（つながっていれば null）。</summary>
    public string? LastError { get; private set; }

    /// <summary>録音は続くが知らせたいこと（切断・つなぎ直し・既定の変更。別スレッドから）。</summary>
    public event Action<string>? Notice;

    /// <summary>自動再開がOFFのとき、デバイスが止まったことを知らせる（別スレッドから）。</summary>
    public event Action<string>? Failed;

    public DeviceInput(IAudioDeviceProvider provider, string key, string label, DeviceStream stream, bool autoResume)
    {
        _provider = provider;
        _key = key;
        Label = label;
        Stream = stream;
        _autoResume = autoResume;
    }

    /// <summary>つなぐ。つながらなくても監視は始める（あとからつながれば自動で再開）。つながったら true。</summary>
    public bool Start()
    {
        bool ok;
        lock (_lock)
        {
            var real = SafeResolve();
            if (real == null) LastError = "既定のデバイスが見つかりません";
            ok = real != null && Connect(real);
            _reportedLost = !ok;
        }
        _poll = new System.Threading.Timer(_ => Poll(), null, 1000, 1000);
        return ok;
    }

    private string? SafeResolve()
    {
        try { return _provider.Resolve(_key); }
        catch (Exception) { return null; }
    }

    private bool Connect(string real)
    {
        IAudioSource? created = null;
        try
        {
            created = _provider.Create(real);
            Stream.SetFormat(created.Format);
            var mine = created;
            created.DataAvailable += Stream.Push;
            created.Stopped += ex => OnSourceStopped(mine, ex);
            created.Start();
            _src = created;
            _srcKey = real;
            Volatile.Write(ref _lost, 0);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            try { created?.Dispose(); } catch (Exception) { }
            return false;
        }
    }

    private void OnSourceStopped(IAudioSource who, Exception? ex)
    {
        if (_stopping || !ReferenceEquals(who, Volatile.Read(ref _src))) return;
        Volatile.Write(ref _lost, 1);
    }

    private void CloseSource()
    {
        var s = _src;
        _src = null;
        _srcKey = null;
        if (s == null) return;
        try { s.DataAvailable -= Stream.Push; } catch (Exception) { }
        Stream.ClearFormat();
        try { s.Stop(); } catch (Exception) { }
        try { s.Dispose(); } catch (Exception) { }
    }

    /// <summary>1秒ごと。外れたデバイスのつなぎ直しと、既定のデバイスの変更への追従。</summary>
    private void Poll()
    {
        if (!Monitor.TryEnter(_lock)) return;
        try
        {
            if (_stopping) return;
            if (_src != null && Volatile.Read(ref _lost) == 1)
            {
                CloseSource();
                if (!_autoResume)
                {
                    Failed?.Invoke("入力デバイスが使えなくなりました: " + Label);
                    return;
                }
                if (!_reportedLost)
                {
                    _reportedLost = true;
                    Notice?.Invoke("デバイスが使えなくなりました。無音で録音を続け、戻ったら自動で再開します: " + Label);
                }
            }
            var wanted = SafeResolve();
            if (_src != null)
            {
                if (AudioKeys.IsDefault(_key) && wanted != null && wanted != _srcKey)
                {
                    CloseSource();
                    if (Connect(wanted)) Notice?.Invoke("既定のデバイスが変わったので切り替えました: " + Label);
                    else _reportedLost = false;   // つながらなければ、次の周期でつなぎ直す
                }
                return;
            }
            if (wanted == null || !_autoResume) return;
            if (Connect(wanted))
            {
                _reportedLost = false;
                Notice?.Invoke("デバイスにつなぎ直しました: " + Label);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>監視をやめてデバイスを閉じる。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        System.Threading.Timer? t;
        lock (_lock)
        {
            _stopping = true;
            t = _poll;
            _poll = null;
        }
        t?.Dispose();
        lock (_lock) CloseSource();
        Stream.Close();
    }

    public void Dispose() => Stop();
}
