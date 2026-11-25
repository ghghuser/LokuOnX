using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Recorder.Core;

namespace Gokko.Core;

/// <summary>LokuOnX の制御API（名前付きパイプ）のクライアント。接続して state を購読し、命令を送る。接続できなければ <see cref="Connected"/> は false のまま（LokuOnX が無くても Gokko4DJ は使える）。</summary>
public sealed class ControlClient : IDisposable
{
    private readonly string _pipeName;
    private readonly object _lock = new();
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private long _nextId;

    public ControlClient(string pipeName) => _pipeName = pipeName;

    public bool Connected { get; private set; }

    /// <summary>状態が届いた（別スレッドから呼ばれる）。</summary>
    public event Action<LokuState>? StateChanged;

    /// <summary>接続が切れた・つながった（別スレッドから呼ばれる）。</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>つなぐ。成功したら true。LokuOnX の設定で「外部のアプリから操作できるようにする」がOFFだと失敗する。</summary>
    public async Task<bool> ConnectAsync(int timeoutMs = 600)
    {
        Disconnect();
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeoutMs).ConfigureAwait(false);
        }
        catch (Exception)
        {
            pipe.Dispose();
            return false;
        }
        var cts = new CancellationTokenSource();
        lock (_lock)
        {
            _pipe = pipe;
            _cts = cts;
            _writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true, NewLine = "\n" };
        }
        Connected = true;
        ConnectionChanged?.Invoke(true);
        _ = Task.Run(() => ReadLoop(pipe, cts.Token));
        try
        {
            await Send("subscribe").ConfigureAwait(false);
            var st = await Send("state").ConfigureAwait(false);
            if (st.ok) StateChanged?.Invoke(LokuState.Parse(st.data));
        }
        catch (Exception) { /* 切れたら ReadLoop が後始末する */ }
        return Connected;
    }

    public void Disconnect()
    {
        NamedPipeClientStream? p;
        CancellationTokenSource? c;
        lock (_lock)
        {
            p = _pipe; c = _cts;
            _pipe = null; _cts = null; _writer = null;
        }
        try { c?.Cancel(); } catch (Exception) { }
        try { p?.Dispose(); } catch (Exception) { }
        FailAllPending();
        if (Connected) { Connected = false; ConnectionChanged?.Invoke(false); }
    }

    public void Dispose() => Disconnect();

    /// <summary>命令を送って応答を待つ（最大3秒）。ok＝成功、error＝失敗の理由（接続なし・タイムアウトを含む）。</summary>
    public async Task<(bool ok, string error, JsonElement data)> Send(string cmd, object? args = null)
    {
        StreamWriter? w;
        lock (_lock) w = _writer;
        if (w == null) return (false, "not connected", default);
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _pending[id] = tcs;
        var dict = new Dictionary<string, object?> { ["id"] = id, ["cmd"] = cmd };
        if (args != null)
            foreach (var p in args.GetType().GetProperties()) dict[p.Name] = p.GetValue(args);
        try
        {
            await w.WriteLineAsync(JsonSerializer.Serialize(dict)).ConfigureAwait(false);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(3000)).ConfigureAwait(false);
            if (done != tcs.Task) return (false, "timeout", default);
            var reply = tcs.Task.Result;
            if (reply.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return (true, "", reply.TryGetProperty("data", out var d) ? d : default);
            var msg = reply.TryGetProperty("error", out var er) && er.TryGetProperty("message", out var m) ? m.GetString() ?? "error" : "error";
            return (false, msg, default);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, default);
        }
        finally
        {
            lock (_lock) _pending.Remove(id);
        }
    }

    private async Task ReadLoop(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            using var rd = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, true);
            while (!ct.IsCancellationRequested)
            {
                var line = await rd.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                JsonElement root;
                try { root = JsonDocument.Parse(line).RootElement.Clone(); }
                catch (JsonException) { continue; }
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (root.TryGetProperty("event", out var ev) && ev.GetString() == "state" && root.TryGetProperty("data", out var data))
                {
                    StateChanged?.Invoke(LokuState.Parse(data));
                }
                else if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var id))
                {
                    TaskCompletionSource<JsonElement>? tcs;
                    lock (_lock) _pending.TryGetValue(id, out tcs);
                    tcs?.TrySetResult(root);
                }
            }
        }
        catch (Exception) { }
        finally
        {
            bool mine;
            lock (_lock) mine = ReferenceEquals(_pipe, pipe);
            if (mine) Disconnect();
        }
    }

    private void FailAllPending()
    {
        List<TaskCompletionSource<JsonElement>> all;
        lock (_lock) { all = _pending.Values.ToList(); _pending.Clear(); }
        foreach (var t in all) t.TrySetCanceled();
    }

    /// <summary>LokuOnX.exe と同じフォルダにある前提のパイプ名。</summary>
    public static string DefaultPipe(string exeDir) => ControlProtocol.PipeName(exeDir, AppInfo.Name);
}
