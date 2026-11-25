using System.Text.Json;
using Gokko.Core;
using Recorder.Core;

/// <summary>Gokko4DJ（M18）の Core: パッドの設定・鳴らし方・ミキサー・LokuOnX 制御クライアント。</summary>
static class GokkoTests
{
    private sealed class FakeHost : IControlHost
    {
        public List<string> Calls = new();
        public string? Fail;
        public Task<object> State() => Task.FromResult<object>(new
        {
            recording = true, paused = false, standby = false, track = 3, masterGainDb = 0.0,
            inputs = new[] { new { index = 1, active = true, gainDb = -2.5, muted = true, fx = new { any = true, on = false } } },
            outputs = new[] { new { index = 1, name = "ミックスA", on = true, running = true, volumeDb = 1.5, fx = new { any = false, on = true } } },
        });
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

    private static PadClip Constant(short v, int frames)
    {
        var pcm = new short[frames * 2];
        Array.Fill(pcm, v);
        return new PadClip(pcm);
    }

    private static float[] Read(PadMixer m, int frames)
    {
        var b = new float[frames * 2];
        m.Read(b, 0, b.Length);
        return b;
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("Gokko4DJ: パッドの設定（M18）");
        {
            var p = new PadConfig("  " + new string('あ', 40) + " ", " \"C:\\se\\a.wav\" ", PadMode.Loop, 99, -1).Normalize();
            check(p.Name.Length == PadConfig.MaxNameLength, "名前は長さの上限で切る");
            check(p.FilePath == "C:\\se\\a.wav", "ファイルの場所から引用符と空白を取る", p.FilePath);
            check(p.VolumeDb == PadConfig.MaxDb, "音量は範囲内へ", p.VolumeDb.ToString());
            check(p.Color == PadConfig.ColorCount - 1, "色の番号は範囲内へ（-1 → 最後）", p.Color.ToString());
            check(new PadConfig("", "C:\\x\\Boom.wav", PadMode.Once, 0, 0).Title == "Boom", "名前が空ならファイル名を出す");
            check(PadConfig.ToGain(PadConfig.MinDb) == 0f && Math.Abs(PadConfig.ToGain(0) - 1f) < 1e-6 && Math.Abs(PadConfig.ToGain(-6.0206) - 0.5f) < 1e-3, "dB → 倍率（左端はミュート）");
            check(PadConfig.Empty.IsEmpty && !p.IsEmpty, "ファイルが無ければ空のパッド");
        }
        section("Gokko4DJ: 設定ファイル（M18）");
        {
            var dir = Path.Combine(Path.GetTempPath(), "gokko-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "gokko.ini");
            try
            {
                var s = new GokkoSettings(path);
                s.OutputDeviceId = "{dev-1}";
                s.MasterDb = -3.5;
                s.Page = 2;
                s.MinimizeToTray = false;
                s.SetLaunchers(new string?[] { "C:\\a\\One.exe", "", "C:\\b\\Two.exe" });
                s.Pads[0] = new PadConfig("ドン", "C:\\se\\don.wav", PadMode.Once, -3, 2);
                s.Pads[PadBank.Id(3, 15)] = new PadConfig("", "C:\\se\\loop.ogg", PadMode.Hold, 4.5, 7);
                s.Save();
                var t = new GokkoSettings(path);
                check(t.OutputDeviceId == "{dev-1}" && t.MasterDb == -3.5 && t.Page == 2 && !t.MinimizeToTray, "全体の設定を保存・復元できる");
                check(t.Launchers.Count == 2 && t.Launchers[1] == "C:\\b\\Two.exe", "登録アプリを保存・復元できる", string.Join("|", t.Launchers));
                check(t.Pads[0] == new PadConfig("ドン", "C:\\se\\don.wav", PadMode.Once, -3, 2), "パッドを保存・復元できる");
                var last = t.Pads[PadBank.Id(3, 15)];
                check(last.Mode == PadMode.Hold && last.VolumeDb == 4.5 && last.Color == 7 && last.Name == "", "最後のページの最後のパッド（押している間だけ）");
                check(t.Pads[1].IsEmpty, "使っていないパッドは空のまま");
                t.Pads[0] = PadConfig.Empty;
                t.Save();
                check(new GokkoSettings(path).Pads[0].IsEmpty, "空にすると設定から消える");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
        section("Gokko4DJ: ミキサー（M18）");
        {
            var m = new PadMixer();
            var buf = Read(m, 64);
            check(buf.All(x => x == 0f), "何も鳴らしていなければ無音");
            m.Start(1, Constant(16384, 2000), false, 0);
            var a = Read(m, 1000);
            check(a[0] == 0f && Math.Abs(a[(PadMixer.FadeInFrames + 10) * 2] - 0.5f) < 1e-3, "鳴らし始めは短いフェードイン、そのあと元の音量", a[(PadMixer.FadeInFrames + 10) * 2].ToString());
            check(m.IsPlaying(1) && m.Progress(1) > 0.4 && m.Progress(1) < 0.6, "鳴っている途中の進み具合", m.Progress(1).ToString());
            Read(m, 2000);
            check(!m.IsPlaying(1), "1回だけは最後まで鳴ったら止まる");
            m.Start(2, Constant(16384, 100), true, 0);
            Read(m, 1000);
            check(m.IsPlaying(2), "ループは終わりまで行っても鳴り続ける");
            m.Stop(2);
            var f = Read(m, PadMixer.FadeOutFrames + 100);
            check(!m.IsPlaying(2) && f[^2] == 0f, "止めると短くフェードアウトして無音になる");
            m.Start(3, Constant(32767, 3000), false, 0);
            m.Start(4, Constant(32767, 3000), false, 0);
            var c = Read(m, 1000);
            check(c.All(x => x <= 1f && x >= -1f) && c[(PadMixer.FadeInFrames + 10) * 2] > 0.99f, "重なった音は±1に切り詰める");
            m.StopAll();
            Read(m, 1000);
            m.Start(5, Constant(16384, 3000), false, -30);
            check(Read(m, 500).All(x => x == 0f), "音量が左端ならミュート");
            m.StopAll();
            Read(m, 1000);
            m.MasterDb = -6.0206;
            m.Start(6, Constant(16384, 3000), false, 0);
            var h = Read(m, 500);
            check(Math.Abs(h[(PadMixer.FadeInFrames + 10) * 2] - 0.25f) < 2e-3, "全体の音量が掛かる", h[(PadMixer.FadeInFrames + 10) * 2].ToString());
            m.StopAll();
            Read(m, 1000);
            m.MasterDb = 0;
            m.Start(7, Constant(16384, 3000), false, 0);
            Read(m, 400);
            m.Start(7, Constant(16384, 3000), false, 0);
            check(m.IsPlaying(7), "同じパッドをもう一度鳴らすと新しい音で鳴る");
            Read(m, PadMixer.FadeOutFrames + 10);
            check(m.Progress(7) > 0 && m.Progress(7) < 0.2, "前の音は止まり、新しい音は頭から", m.Progress(7).ToString());
        }
        section("Gokko4DJ: パッドの鳴らし方（M18）");
        {
            var bank = new PadBank();
            var mixer = new PadMixer();
            var loads = new List<string>();
            var ctl = new PadController(bank, mixer, path =>
            {
                loads.Add(path);
                return path.Contains("bad") ? null : Constant(10000, 100000);
            });
            check(ctl.Press(0) == PadPress.Empty, "空のパッドは何も起きない");
            bank[1] = new PadConfig("", "bad.wav", PadMode.Once, 0, 0);
            check(ctl.Press(1) == PadPress.LoadFailed, "読めないファイルは失敗を返す");
            bank[2] = new PadConfig("", "once.wav", PadMode.Once, 0, 0);
            check(ctl.Press(2) == PadPress.Started && mixer.IsPlaying(2), "1回だけ: 押すと鳴る");
            ctl.Release(2);
            check(mixer.IsPlaying(2), "1回だけ: 離しても鳴り続ける");
            ctl.Press(2);
            check(loads.Count(x => x == "once.wav") == 1, "読み込みは1回だけ（覚えている）");
            bank[3] = new PadConfig("", "loop.wav", PadMode.Loop, 0, 0);
            check(ctl.Press(3) == PadPress.Started && mixer.IsPlaying(3), "ループ: 押すと鳴る");
            ctl.Release(3);
            check(mixer.IsPlaying(3), "ループ: 離しても鳴る");
            check(ctl.Press(3) == PadPress.Stopped && !mixer.IsPlaying(3), "ループ: もう一度押すと止まる");
            bank[4] = new PadConfig("", "hold.wav", PadMode.Hold, 0, 0);
            ctl.Press(4);
            check(mixer.IsPlaying(4), "押している間だけ: 押すと鳴る");
            ctl.Release(4);
            check(!mixer.IsPlaying(4), "押している間だけ: 離すと止まる");
            ctl.Forget("once.wav");
            ctl.Press(2);
            check(loads.Count(x => x == "once.wav") == 2, "忘れたあとは読み直す");
        }
        section("Gokko4DJ: LokuOnX の状態の読み取り（M18）");
        {
            var s = LokuState.Parse(JsonDocument.Parse("{\"recording\":true,\"track\":2,\"inputs\":[{\"index\":2,\"active\":true,\"gainDb\":-3,\"muted\":true,\"fx\":{\"any\":true,\"on\":false}}],\"outputs\":[{\"index\":1,\"name\":\"A\",\"on\":true,\"volumeDb\":1.5}]}").RootElement);
            check(s.Recording && !s.Paused && s.Track == 2, "録音の状態");
            check(s.Inputs.Count == 1 && s.Inputs[0] is { Index: 2, Active: true, GainDb: -3, Muted: true, FxAny: true, FxOn: false }, "入力の状態");
            check(s.Outputs.Count == 1 && s.Outputs[0].Name == "A" && s.Outputs[0].On && s.Outputs[0].VolumeDb == 1.5 && s.Outputs[0].FxOn, "出力の状態（無い項目は初期値）");
            check(LokuState.Parse(JsonDocument.Parse("[1]").RootElement).Inputs.Count == 0, "オブジェクトでなければ空の状態");
        }
        section("Gokko4DJ: LokuOnX へ接続（名前付きパイプ。M18）");
        {
            var dir = Path.Combine(Path.GetTempPath(), "gokko-pipe-" + Guid.NewGuid().ToString("N"));
            var host = new FakeHost();
            using var server = new ControlServer(ControlProtocol.PipeName(dir, "LokuOnX"), new ControlDispatcher(host, "LokuOnX", "1.0"));
            using var client = ControlClient.ForPipe(dir);
            try
            {
                Task.Run(async () =>
                {
                    check(!await client.ConnectAsync(), "LokuOnX が待ち受けていなければ接続できない（落ちない）");
                    server.Start();
                    check(await client.ConnectAsync() && client.Connected, "待ち受けていれば接続できる");
                    check(client.State.Recording && client.State.Track == 3 && client.State.Inputs.Count == 1 && client.State.Outputs[0].Name == "ミックスA", "つないだ直後に状態を受け取る");
                    var r = await client.SendAsync("rec.start");
                    check(r.Ok && host.Calls.Contains("start"), "録音の開始が届く");
                    r = await client.SendAsync("fx.on", ("target", "input"), ("index", 2), ("on", false));
                    check(r.Ok && host.Calls.Contains("fx in 2 False"), "入力FXのオン・オフが届く");
                    r = await client.SendAsync("input.gain", ("index", 1), ("db", -4.5));
                    check(r.Ok && host.Calls.Contains("gain 1 -4.5"), "入力ゲインが届く");
                    r = await client.SendAsync("output.volume", ("index", 1), ("db", 99.0));
                    check(!r.Ok && r.Error.Length > 0, "範囲外の値は失敗の理由が返る", r.Error);
                    host.Fail = "録音中ではありません";
                    r = await client.SendAsync("rec.stop");
                    check(!r.Ok && r.Error == "録音中ではありません", "実行できなかった理由が返る", r.Error);
                    host.Fail = null;
                    var got = new TaskCompletionSource<LokuState>(TaskCreationOptions.RunContinuationsAsynchronously);
                    client.StateChanged += st => got.TrySetResult(st);
                    server.Publish(new { recording = false, paused = true, inputs = Array.Empty<object>(), outputs = Array.Empty<object>() });
                    var ev = await Task.WhenAny(got.Task, Task.Delay(5000)) == got.Task ? got.Task.Result : null;
                    check(ev != null && ev.Paused && !ev.Recording, "状態の変化の通知を受け取る");
                    var dropped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    client.ConnectionChanged += c => { if (!c) dropped.TrySetResult(true); };
                    server.Stop();
                    check(await Task.WhenAny(dropped.Task, Task.Delay(5000)) == dropped.Task && !client.Connected, "LokuOnX が終わったら切断を知らせる");
                    check(!(await client.SendAsync("rec.start")).Ok, "切れているあいだの命令は失敗を返す");
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                check(false, "LokuOnX への接続", ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
