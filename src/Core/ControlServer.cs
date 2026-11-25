using System.IO.Pipes;
using System.Text;

namespace Recorder.Core;

/// <summary>
/// 制御API（M17）の名前付きパイプのサーバー。1行＝1件のJSONで受けて <see cref="ControlDispatcher"/> に渡し、応答を1行で返す。
/// 同じ Windows ユーザーのプロセスだけが接続できる（CurrentUserOnly）。同時接続は3つまで。
/// "subscribe" を送った接続には、<see cref="Publish"/> した状態の変化を { "event":"state" } で流す。
/// </summary>
public sealed class ControlServer : IDisposable
{
    private const int MaxInstances = 4;

    private sealed class Conn
    {
        public NamedPipeServerStream Pipe { get; }
        public StreamReader Reader { get; }
        public StreamWriter Writer { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public volatile bool Subscribed;

        public Conn(NamedPipeServerStream pipe)
        {
            Pipe = pipe;
            var enc = new UTF8Encoding(false);
            Reader = new StreamReader(pipe, enc, false, 1024, true);
            Writer = new StreamWriter(pipe, enc, 1024, true) { AutoFlush = true, NewLine = "\n" };
        }

        public async Task Send(string line, CancellationToken ct)
        {
            await WriteLock.WaitAsync(ct).ConfigureAwait(false);
            try { await Writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false); }
            finally { WriteLock.Release(); }
        }

        public void Close()
        {
            try { Pipe.Dispose(); } catch (Exception) { }
        }
    }

    private readonly string _name;
    private readonly ControlDispatcher _dispatcher;
    private readonly List<Conn> _conns = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public ControlServer(string pipeName, ControlDispatcher dispatcher)
    {
        _name = pipeName;
        _dispatcher = dispatcher;
    }

    public bool Running => _cts != null;

    /// <summary>待ち受けを始める（すでに始まっていれば何もしない）。</summary>
    public void Start()
    {
        if (_cts != null) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Task.Run(() => AcceptLoop(cts.Token));
    }

    /// <summary>待ち受けをやめて、つながっている接続をすべて閉じる。</summary>
    public void Stop()
    {
        var cts = _cts;
        if (cts == null) return;
        _cts = null;
        try { cts.Cancel(); } catch (Exception) { }
        List<Conn> all;
        lock (_lock) { all = _conns.ToList(); _conns.Clear(); }
        foreach (var c in all) c.Close();
        cts.Dispose();
    }

    public void Dispose() => Stop();

    /// <summary>購読中（subscribe 済み）の接続すべてへ、状態の変化を流す。</summary>
    public void Publish(object state)
    {
        var ct = _cts?.Token;
        if (ct == null) return;
        var line = ControlProtocol.Event("state", state);
        List<Conn> targets;
        lock (_lock) targets = _conns.Where(c => c.Subscribed).ToList();
        foreach (var c in targets)
            _ = Task.Run(async () =>
            {
                try { await c.Send(line, ct.Value).ConfigureAwait(false); }
                catch (Exception) { Drop(c); }
            });
    }

    /// <summary>購読している接続があるか（無ければ状態を組み立てなくてよい）。</summary>
    public bool HasSubscribers
    {
        get { lock (_lock) return _conns.Any(c => c.Subscribed); }
    }

    private void Drop(Conn c)
    {
        lock (_lock) _conns.Remove(c);
        c.Close();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var c = new Conn(pipe);
                lock (_lock) _conns.Add(c);
                _ = Task.Run(() => Serve(c, ct));
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception)
            {
                pipe?.Dispose();
                try { await Task.Delay(500, ct).ConfigureAwait(false); }   // 同時接続が上限のときなど。少し待ってやり直す
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task Serve(Conn c, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await ReadLine(c.Reader, ct).ConfigureAwait(false);
                if (line == null) break;   // 切断・長すぎる行
                if (line.Length == 0) continue;
                string reply;
                if (!ControlProtocol.TryParse(line, out var req, out var error))
                {
                    reply = ControlProtocol.Fail(null, "bad_request", error);
                }
                else if (req.Cmd == "subscribe" || req.Cmd == "unsubscribe")
                {
                    c.Subscribed = req.Cmd == "subscribe";
                    reply = ControlProtocol.Ok(req.Id);
                }
                else
                {
                    reply = await _dispatcher.Handle(req).ConfigureAwait(false);
                }
                await c.Send(reply, ct).ConfigureAwait(false);
            }
        }
        catch (Exception) { }
        finally { Drop(c); }
    }

    /// <summary>1行を読む。切断されたら null、長すぎる行（<see cref="ControlProtocol.MaxLineChars"/> 超）も null（接続を切る）。</summary>
    private static async Task<string?> ReadLine(StreamReader r, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new char[1];
        while (true)
        {
            int n = await r.ReadAsync(one.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0) return null;
            if (one[0] == '\n') return sb.ToString().TrimEnd('\r');
            if (sb.Length >= ControlProtocol.MaxLineChars) return null;
            sb.Append(one[0]);
        }
    }
}
