using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Recorder.Core;

/// <summary>アプリランチャーの一覧処理と、制御API（M17: プロトコル・命令の振り分け・名前付きパイプの往復）のテスト。</summary>
static class ControlTests
{
    private sealed class FakeHost : IControlHost
    {
        public List<string> Calls = new();
        public string? Fail;
        public Task<object> State() => Task.FromResult<object>(new { recording = false });
        private Task<string?> Rec(string s) { Calls.Add(s); return Task.FromResult(Fail); }
        public Task<string?> RecStart() => Rec("start");
        public Task<string?> RecStop() => Rec("stop");
        public Task<string?> RecPause(bool pause) => Rec(pause ? "pause" : "resume");
        public Task<string?> RecSplit() => Rec("split");
        public Task<string?> FxOn(bool output, int index, bool on) => Rec($"fx {(output ? "out" : "in")} {index} {on}");
        public Task<string?> OutputOn(int index, bool on) => Rec($"out {index} {on}");
        public Task<string?> OutputVolume(int index, double db) => Rec($"vol {index} {db}");
        public Task<string?> InputGain(int index, double db) => Rec($"gain {index} {db}");
        public Task<string?> InputMute(int index, bool mute) => Rec($"mute {index} {mute}");
        public Task<string?> MasterGain(double db) => Rec($"master {db}");
    }

    private static string Req(ControlDispatcher d, string json)
    {
        ControlProtocol.TryParse(json, out var r, out _);
        return d.Handle(r).GetAwaiter().GetResult();
    }

    private static bool IsOk(string reply) => JsonDocument.Parse(reply).RootElement.GetProperty("ok").GetBoolean();
    private static string Code(string reply) => JsonDocument.Parse(reply).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    public static void Run(CheckFn check, Action<string> section)
    {
        section("アプリランチャーの一覧（M17）");
        {
            var l = LauncherList.Clean(new string?[] { " \"C:\\a\\b.exe\" ", "", null, "x", "y", "z", "w", "v" });
            check(l.Count == 5 && l[0] == "C:\\a\\b.exe" && l[1] == "x", "空を捨て、引用符と空白を取り、5個まで", string.Join("|", l));
            check(LauncherList.Clean(new string?[] { new string('a', 2000) }).Count == 0, "長すぎるパスは捨てる");
            check(LauncherList.Title("C:\\Program Files\\Foo\\Bar.exe") == "Bar", "名前は拡張子とフォルダを除く");
            check(LauncherList.Title("C:\\Foo\\") == "Foo", "末尾の\\は無視する");
        }
        section("制御APIの書式（M17）");
        {
            check(ControlProtocol.TryParse("{\"id\":3,\"cmd\":\"rec.start\"}", out var r, out _) && r.Id == 3 && r.Cmd == "rec.start", "要求を読める");
            check(!ControlProtocol.TryParse("not json", out _, out _), "JSONでなければ失敗");
            check(!ControlProtocol.TryParse("{\"id\":1}", out _, out var e1) && e1.Contains("cmd"), "cmd が無ければ失敗");
            check(!ControlProtocol.TryParse("[1]", out _, out _), "オブジェクト以外は失敗");
            check(!ControlProtocol.TryParse("{\"id\":\"a\",\"cmd\":\"x\"}", out _, out _), "id が数でなければ失敗");
            check(ControlProtocol.PipeName("C:\\A", "LokuOnX") == ControlProtocol.PipeName("c:\\a\\", "LokuOnX"), "パイプ名は大小文字・末尾の\\を区別しない");
            check(ControlProtocol.PipeName("C:\\A", "LokuOnX") != ControlProtocol.PipeName("C:\\B", "LokuOnX"), "フォルダが違えばパイプ名も違う");
            var ok = ControlProtocol.Ok(7, new { a = 1 });
            check(ok.Contains("\"ok\":true") && ok.Contains("\"id\":7") && !ok.Contains('\n'), "応答は1行");
        }
        section("制御APIの命令（M17）");
        {
            var host = new FakeHost();
            var d = new ControlDispatcher(host, "LokuOnX", "1.0");
            check(IsOk(Req(d, "{\"id\":1,\"cmd\":\"hello\"}")), "hello");
            check(IsOk(Req(d, "{\"id\":1,\"cmd\":\"state\"}")), "state");
            Req(d, "{\"cmd\":\"rec.start\"}"); Req(d, "{\"cmd\":\"rec.pause\"}"); Req(d, "{\"cmd\":\"rec.resume\"}"); Req(d, "{\"cmd\":\"rec.split\"}"); Req(d, "{\"cmd\":\"rec.stop\"}");
            check(string.Join(",", host.Calls) == "start,pause,resume,split,stop", "録音の命令が順に届く", string.Join(",", host.Calls));
            host.Calls.Clear();
            check(IsOk(Req(d, "{\"cmd\":\"fx.on\",\"target\":\"output\",\"index\":2,\"on\":false}")) && host.Calls[0] == "fx out 1 False", "fx.on は index を0始まりにして渡す", host.Calls[0]);
            check(IsOk(Req(d, "{\"cmd\":\"input.gain\",\"index\":1,\"db\":-6.5}")) && host.Calls[1] == "gain 0 -6.5", "input.gain");
            check(IsOk(Req(d, "{\"cmd\":\"master.gain\",\"db\":3}")) && host.Calls[2] == "master 3", "master.gain");
            int n = host.Calls.Count;
            check(Code(Req(d, "{\"cmd\":\"input.gain\",\"index\":5,\"db\":0}")) == "bad_request", "index 範囲外は bad_request");
            check(Code(Req(d, "{\"cmd\":\"input.gain\",\"index\":1,\"db\":99}")) == "bad_request", "db 範囲外は bad_request");
            check(Code(Req(d, "{\"cmd\":\"fx.on\",\"target\":\"x\",\"index\":1,\"on\":true}")) == "bad_request", "target が不正");
            check(Code(Req(d, "{\"cmd\":\"output.on\",\"index\":1}")) == "bad_request", "on が無い");
            check(host.Calls.Count == n, "不正な要求は操作先へ届かない");
            check(Code(Req(d, "{\"cmd\":\"nope\"}")) == "unknown_cmd", "未知の命令");
            host.Fail = "録音中ではありません";
            var f = Req(d, "{\"cmd\":\"rec.stop\"}");
            check(!IsOk(f) && Code(f) == "failed" && f.Contains("録音中ではありません"), "操作先の失敗理由が返る");
        }
        section("制御APIの名前付きパイプ（M17）");
        {
            var host = new FakeHost();
            var name = "LokuOnXTest." + Guid.NewGuid().ToString("N");
            using var server = new ControlServer(name, new ControlDispatcher(host, "LokuOnX", "1.0"));
            server.Start();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                Task.Run(async () =>
                {
                    using var cli = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await cli.ConnectAsync(10000, cts.Token);
                    var enc = new UTF8Encoding(false);
                    using var rd = new StreamReader(cli, enc);
                    using var wr = new StreamWriter(cli, enc) { AutoFlush = true, NewLine = "\n" };
                    await wr.WriteLineAsync("{\"id\":1,\"cmd\":\"hello\"}");
                    var a = await rd.ReadLineAsync(cts.Token);
                    check(a != null && a.Contains("\"ok\":true") && a.Contains("LokuOnX"), "パイプ越しに hello の応答が返る", a ?? "(null)");
                    await wr.WriteLineAsync("{\"id\":2,\"cmd\":\"rec.start\"}");
                    var b = await rd.ReadLineAsync(cts.Token);
                    check(b != null && b.Contains("\"id\":2") && host.Calls.Contains("start"), "パイプ越しに命令が届く", b ?? "(null)");
                    await wr.WriteLineAsync("garbage");
                    var c = await rd.ReadLineAsync(cts.Token);
                    check(c != null && c.Contains("bad_request"), "壊れた行には bad_request を返して接続は続く", c ?? "(null)");
                    await wr.WriteLineAsync("{\"id\":3,\"cmd\":\"subscribe\"}");
                    await rd.ReadLineAsync(cts.Token);
                    server.Publish(new { recording = true });
                    var ev = await rd.ReadLineAsync(cts.Token);
                    check(ev != null && ev.Contains("\"event\":\"state\"") && ev.Contains("true"), "購読すると状態の変化が届く", ev ?? "(null)");
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                check(false, "名前付きパイプの往復", ex.GetType().Name + ": " + ex.Message);
            }
            server.Stop();
            check(!server.Running, "Stop で待ち受けが止まる");
        }
    }
}
