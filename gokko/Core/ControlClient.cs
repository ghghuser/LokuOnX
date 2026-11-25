using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Recorder.Core;

namespace Gokko.Core;

/// <summary>命令への返事。Ok＝成功、Error＝失敗の理由（通信の失敗も含む）。</summary>
public sealed record ControlReply(bool Ok, string Error);

/// <summary>
/// LokuOnX の制御API（名前付きパイプ。1行＝1件のJSON）のクライアント。つながったら hello → subscribe → state を送り、
/// 状態の変化を <see cref="StateChanged"/> で伝える。切れたら <see cref="ConnectionChanged"/>。イベントは別スレッドで起きる。
/// </summary>
public sealed class ControlClient : IDisposable
{
    private readonly Func<CancellationToken, Task<Stream>> _open;
    private readonly object _lock = new();
    private readonly Dictionary<long, TaskCompletionSource<ControlReply>> _pending = new();
    private Stream? _stream;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private long _nextId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public event Action<LokuState>? StateChanged;
    public event Action<bool>? ConnectionChanged;

    public bool Connected { get; private set; }
    public LokuState State { get; private set; } = LokuState.Empty;

    public ControlClient(Func<CancellationToken, Task<Stream>> open) => _open = open;

    /// <summary>LokuOnX（同じフォルダの exe）の名前付きパイプへつなぐクライアント。</summary>
    public static ControlClient ForPipe(string exeDir, string appName = "LokuOnX")
    {
        var name = ControlProtocol.PipeName(exeDir, appName);
        return new ControlClient(async ct =>
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(300, ct).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        });
    }

    /// <summary>つなぐ（すでにつながっていれば true）。LokuOnX が無い・待ち受けがOFFのときは false。</summary>
    public async Task<bool> ConnectAsync()
    {
        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Connected) return true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Stream s;
            try { s = await _open(timeout.Token).ConfigureAwait(false); }
            catch (Exception) { return false; }

            var cts = new CancellationTokenSource();
            _stream = s;
            _writer = new StreamWriter(s, new UTF8Encoding(false), 1024, true) { AutoFlush = true, NewLine = "\n" };
            _cts = cts;
            _ = Task.Run(() => ReadLoop(s, cts.Token));

            var hello = await SendAsync("hello").ConfigureAwait(false);
            var sub = hello.Ok ? await SendAsync("subscribe").ConfigureAwait(false) : hello;
            var st = sub.Ok ? await SendAsync("state").ConfigureAwait(false) : sub;
            if (!st.Ok)
            {
                Disconnect();
                return false;
            }
            Connected = true;
            ConnectionChanged?.Invoke(true);
            return true;
        }
        finally { _connectLock.Release(); }
    }

    /// <summary>命令を送る。args は { "index":1, "on":true } のような引数（無くてもよい）。</summary>
    public Task<ControlReply> SendAsync(string cmd) => SendAsync(cmd, (IReadOnlyDictionary<string, object?>?)null);

    public async Task<ControlReply> SendAsync(string cmd, IReadOnlyDictionary<string, object?>? args)
    {
        var w = _writer;
        if (w == null) return new ControlReply(false, "not connected");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<ControlReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _pending[id] = tcs;
        var body = new Dictionary<string, object?> { ["id"] = id, ["cmd"] = cmd };
        if (args != null) foreach (var kv in args) body[kv.Key] = kv.Value;
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try { await w.WriteLineAsync(JsonSerializer.Serialize(body)).ConfigureAwait(false); }
            finally { _writeLock.Release(); }
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            if (done != tcs.Task) return new ControlReply(false, "timeout");
            return tcs.Task.Result;
        }
        catch (Exception ex)
        {
            Disconnect();
            return new ControlReply(false, ex.Message);
        }
        finally { lock (_lock) _pending.Remove(id); }
    }

    public Task<ControlReply> SendAsync(string cmd, params (string Key, object? Value)[] args) =>
        SendAsync(cmd, args.ToDictionary(a => a.Key, a => a.Value));

    private async Task ReadLoop(Stream s, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(s, new UTF8Encoding(false), false, 4096, true);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                Handle(line);
            }
        }
        catch (Exception) { }
        if (!ct.IsCancellationRequested) Disconnect();
    }

    private void Handle(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("event", out var ev) && ev.GetString() == "state" && root.TryGetProperty("data", out var d))
            {
                Publish(LokuState.Parse(d));
                return;
            }
            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt64(out var id)) return;
            TaskCompletionSource<ControlReply>? tcs;
            lock (_lock) _pending.TryGetValue(id, out tcs);
            if (tcs == null) return;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (ok && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("inputs", out _))
                Publish(LokuState.Parse(data));   // state の応答
            var err = "";
            if (!ok && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m))
                err = m.GetString() ?? "";
            tcs.TrySetResult(new ControlReply(ok, err));
        }
        catch (JsonException) { }
    }

    private void Publish(LokuState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    private void Disconnect()
    {
        var was = Connected;
        Connected = false;
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (Exception) { }
        try { _stream?.Dispose(); } catch (Exception) { }
        _stream = null;
        _writer = null;
        List<TaskCompletionSource<ControlReply>> waiting;
        lock (_lock) { waiting = _pending.Values.ToList(); _pending.Clear(); }
        foreach (var t in waiting) t.TrySetResult(new ControlReply(false, "disconnected"));
        State = LokuState.Empty;
        if (was) ConnectionChanged?.Invoke(false);
    }

    public void Dispose() => Disconnect();
}
