using System.Text.RegularExpressions;
using Gokko.Core;
using Recorder.Core;

/// <summary>Gokko4DJ（M18）の純粋ロジック: パッド設定・ミキサー・押す／離す・LokuOnX 状態の読み取り・制御APIのクライアント。</summary>
static class GokkoTests
{
    private sealed class Host : IControlHost
    {
        public List<string> Calls = new();
        public Task<object> State() => Task.FromResult<object>(new
        {
            recording = true, paused = false, standby = false, track = 3,
            inputs = new[] { new { index = 1, active = true, gainDb = -3.5, muted = true, fx = new { any = true, on = false } } },
            outputs = new[] { new { index = 1, name = "A", on = true, volumeDb = -6.0 } },
        });
        private Task<string?> Rec(string s) { Calls.Add(s); return Task.FromResult<string?>(null); }
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

    private static PadClip Clip(int frames, short value)
    {
        var pcm = new short[frames * 2];
        Array.Fill(pcm, value);
        return new PadClip(pcm);
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("Gokko4DJ: パッドの設定（M18）");
        {
            var p = new PadConfig("  " + new string('あ', 40), "\"C:\\se\\a.wav\"", (PadMode)99, 99, -3).Normalize();
            check(p.Name.Length == PadConfig.MaxNameLength, "名前は24文字まで");
            check(p.FilePath == "C:\\se\\a.wav", "ファイルの引用符を取る");
            check(p.Mode == PadMode.Once, "不正な鳴らし方は1回だけに戻す");
            check(p.VolumeDb == PadConfig.MaxDb, "音量は範囲内へ", p.VolumeDb.ToString());
            check(p.Color >= 0 && p.Color < PadConfig.ColorCount, "色は範囲内へ", p.Color.ToString());
            check(new PadConfig("", "C:\\x\\Boom.wav", PadMode.Once, 0, 0).Title == "Boom", "名前が空ならファイル名");
            check(PadConfig.Empty.IsEmpty, "空のパッド");
            check(PadConfig.ToGain(PadConfig.MinDb) == 0f && Math.Abs(PadConfig.ToGain(0) - 1f) < 1e-6 && Math.Abs(PadConfig.ToGain(-6) - 0.501f) < 0.01f, "dB→倍率（左端はミュート）");
        }
        section("Gokko4DJ: 設定の保存と読み込み（M18）");
        {
            var path = Path.Combine(Path.GetTempPath(), "gokko-test-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var s = new GokkoSettings(path);
                s.OutputDeviceId = "dev-1";
                s.MasterDb = -4.5;
                s.Cols = 6; s.Rows = 3; s.Theme = "dark"; s.Borders[GokkoSettings.BorderKey(true, 1, 2)] = 3; s.Borders[GokkoSettings.BorderKey(false, 0, 0)] = 7;
                s.Launcher = new List<string> { "C:\\a\\b.exe" };
                s.Bank[PadBank.Id(1, 5)] = new PadConfig("Boom", "C:\\se\\boom.wav", PadMode.Loop, -6, 3);
                s.Bank[0] = new PadConfig("", "C:\\se\\hold.mp3", PadMode.Hold, 2.5, 7);
                s.Save();
                var r = new GokkoSettings(path);
                var p = r.Bank[PadBank.Id(1, 5)];
                check(r.OutputDeviceId == "dev-1" && r.MasterDb == -4.5, "出力・全体の音量を読み戻せる");
                check(r.Cols == 6 && r.Rows == 3 && r.Theme == "dark", "タイルの数・画面の色を読み戻せる");
                check(r.Borders.Count == 2 && r.Borders[GokkoSettings.BorderKey(true, 1, 2)] == 3 && r.Borders[GokkoSettings.BorderKey(false, 0, 0)] == 7, "枠の色を読み戻せる");
                check(new GokkoSettings(path + ".none").Cols == 4 && new GokkoSettings(path + ".none").Rows == 4, "初期のタイルは4×4");
                check(r.Launcher.Count == 1 && r.Launcher[0] == "C:\\a\\b.exe", "ランチャーの登録を読み戻せる");
                check(p.Name == "Boom" && p.Mode == PadMode.Loop && p.VolumeDb == -6 && p.Color == 3, "パッドを読み戻せる");
                check(r.Bank[0].Mode == PadMode.Hold && r.Bank[0].VolumeDb == 2.5, "別のパッド（押している間だけ）も読み戻せる");
                check(r.Bank[1].IsEmpty, "使っていないパッドは空のまま");
                r.Bank[0] = PadConfig.Empty;
                r.Save();
                check(new GokkoSettings(path).Bank[0].IsEmpty, "空にして保存すると消える");
            }
            finally
            {
                foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(f)) File.Delete(f);
            }
        }
        section("Gokko4DJ: ミキサー（M18）");
        {
            var m = new PadMixer();
            var buf = new float[200];
            m.Read(buf, 0, buf.Length);
            check(buf.All(x => x == 0f), "何も鳴っていなければ無音");
            m.Start(1, Clip(10000, 16384), false, 0);
            m.Read(buf, 0, buf.Length);
            check(Math.Abs(buf[198] - 0.5f) < 0.001f && m.IsPlaying(1), "鳴らすと音が出る（フェードインのあとは原音の大きさ）", buf[198].ToString());
            check(buf[0] == 0f && buf[1] == 0f, "頭は無音から立ち上がる（プツッを防ぐ）");
            m.Start(2, Clip(10000, 30000), false, 0);
            Array.Clear(buf);
            m.Read(buf, 0, buf.Length);
            m.Read(buf, 0, buf.Length);
            check(buf.All(x => x <= 1f && x >= -1f) && buf[190] == 1f || buf[190] > 0.9f, "足し合わせて ±1 に切り詰める", buf[190].ToString());
            m.StopAll();
            for (var i = 0; i < 4; i++) m.Read(buf, 0, buf.Length);
            check(!m.IsPlaying(1) && !m.IsPlaying(2), "全停止でフェードアウトして止まる");
            m.Read(buf, 0, buf.Length);
            check(buf.All(x => x == 0f), "止まったあとは無音");

            m.Start(3, Clip(50, 10000), false, 0);
            for (var i = 0; i < 3; i++) m.Read(buf, 0, buf.Length);
            check(!m.IsPlaying(3), "1回だけの音は最後まで鳴ると終わる");
            m.Start(4, Clip(50, 10000), true, 0);
            for (var i = 0; i < 10; i++) m.Read(buf, 0, buf.Length);
            check(m.IsPlaying(4), "ループは終わらない");
            m.Stop(4);
            for (var i = 0; i < 4; i++) m.Read(buf, 0, buf.Length);
            check(!m.IsPlaying(4), "ループは Stop で止まる");

            m.MasterDb = PadConfig.MinDb;
            m.Start(5, Clip(10000, 16384), false, 0);
            m.Read(buf, 0, buf.Length);
            check(buf.All(x => x == 0f), "全体の音量が左端ならミュート");
            m.MasterDb = 0;
            m.StopAll();
            for (var i = 0; i < 4; i++) m.Read(buf, 0, buf.Length);

            m.Start(6, Clip(10000, 16384), false, -6);
            m.Read(buf, 0, buf.Length);
            check(Math.Abs(buf[190] - 0.25f) < 0.02f, "パッドごとの音量が掛かる", buf[190].ToString());
        }
        section("Gokko4DJ: パッドの押す・離す（M18）");
        {
            var bank = new PadBank();
            var mixer = new PadMixer();
            var loads = 0;
            var c = new PadController(bank, mixer, _ => { loads++; return Clip(100000, 8000); });
            var buf = new float[200];
            bank[0] = new PadConfig("", "a.wav", PadMode.Once, 0, 0);
            bank[1] = new PadConfig("", "b.wav", PadMode.Loop, 0, 0);
            bank[2] = new PadConfig("", "c.wav", PadMode.Hold, 0, 0);
            check(!c.Press(5), "空のパッドは鳴らない");
            check(c.Press(0) && mixer.IsPlaying(0), "1回だけ: 押すと鳴る");
            c.Release(0);
            check(mixer.IsPlaying(0), "1回だけ: 離しても止まらない");
            c.Press(0);
            check(!mixer.IsPlaying(0), "1回だけ: 鳴っているあいだにもう一度押すと止まる");
            c.Press(0);
            check(mixer.IsPlaying(0), "1回だけ: 止めたあとの押し直しでまた鳴る（連打できる）");
            check(c.Press(1) && mixer.IsPlaying(1), "ループ: 押すと始まる");
            c.Release(1);
            check(mixer.IsPlaying(1), "ループ: 離しても続く");
            c.Press(1);
            check(!mixer.IsPlaying(1), "ループ: もう一度押すと止まる");
            check(c.Press(2) && mixer.IsPlaying(2), "押している間だけ: 押すと鳴る");
            var before = loads;
            c.Press(2);
            check(loads == before, "押しっぱなしの繰り返しは無視する（読み込みは増えない）");
            c.Release(2);
            check(!mixer.IsPlaying(2), "押している間だけ: 離すと止まる");
            c.StopAll();
            check(!mixer.IsPlaying(0), "全停止");
            mixer.Read(buf, 0, buf.Length);
            check(buf.All(x => x == 0f), "全停止は即座に無音になる（フェードを待たない）");
            var bad = new PadController(bank, mixer, _ => null);
            check(!bad.Press(0), "読み込めなければ false");

            // 鳴っている途中で鳴らし方を変えても、止められる（実機で止まらなくなった不具合の再現）
            c.Press(0);
            bank[0] = new PadConfig("", "a.wav", PadMode.Hold, 0, 0);
            c.Forget(0);
            check(!mixer.IsPlaying(0), "設定を変えると、鳴っていた音は止まる");
            c.Press(0);
            c.Press(0);
            c.StopAll();
            mixer.Read(buf, 0, buf.Length);
            check(buf.All(x => x == 0f) && !mixer.IsPlaying(0), "押している間だけに変えたあとでも、全停止で止まる");
            c.Press(0);
            c.Forget(0);
            bank[0] = new PadConfig("", "a.wav", PadMode.Loop, 0, 0);
            c.Press(0);
            c.StopAll();
            check(!mixer.IsPlaying(0), "ループに変えて鳴らしたあとでも全停止で止まる");
            bank.Swap(0, 3);
            check(bank[0].IsEmpty && bank[3].Mode == PadMode.Loop, "パッドを入れ替えられる");
        }
        section("Gokko4DJ: LokuOnX の状態の読み取り（M18）");
        {
            var json = ControlProtocol.Ok(1, new
            {
                recording = true, paused = true, standby = false, track = 7,
                inputs = new[] { new { index = 2, active = true, gainDb = -3.5, muted = true, fx = new { any = true, on = false } } },
                outputs = new[] { new { index = 1, name = "スピーカー", on = true, volumeDb = -6.0 } },
            });
            var data = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("data");
            var s = LokuState.Parse(data);
            check(s.Recording && s.Paused && !s.Standby && s.Track == 7, "録音の状態");
            check(s.Inputs.Count == 1 && s.Inputs[0].Index == 2 && s.Inputs[0].GainDb == -3.5 && s.Inputs[0].Muted && s.Inputs[0].FxAny && !s.Inputs[0].FxOn, "入力の状態");
            check(s.Outputs.Count == 1 && s.Outputs[0].Name == "スピーカー" && s.Outputs[0].On && s.Outputs[0].VolumeDb == -6, "出力の状態");
            var empty = LokuState.Parse(System.Text.Json.JsonDocument.Parse("{}").RootElement);
            check(!empty.Recording && empty.Inputs.Count == 0 && empty.Track == null, "空でも読める");
        }
        section("Gokko4DJ: 制御APIのクライアント（M18。名前付きパイプの実接続）");
        {
            var host = new Host();
            var name = "GokkoTest." + Guid.NewGuid().ToString("N");
            using var server = new ControlServer(name, new ControlDispatcher(host, "LokuOnX", "1.0"));
            server.Start();
            using var cli = new ControlClient(name);
            try
            {
                LokuState? got = null;
                cli.StateChanged += st => got = st;
                var ok = cli.ConnectAsync(5000).GetAwaiter().GetResult();
                check(ok && cli.Connected, "つながる");
                check(got != null && got.Recording && got.Track == 3, "つないだ直後に state が届く");
                var r = cli.Send("fx.on", new { target = "input", index = 1, on = false }).GetAwaiter().GetResult();
                check(r.ok && host.Calls.Contains("fx in 0 False"), "FXオン・オフの命令が届く", r.error);
                r = cli.Send("input.gain", new { index = 2, db = -4.5 }).GetAwaiter().GetResult();
                check(r.ok && host.Calls.Contains("gain 1 -4.5"), "入力ゲインの命令が届く", r.error);
                r = cli.Send("rec.split").GetAwaiter().GetResult();
                check(r.ok && host.Calls.Contains("split"), "録音の分割の命令が届く", r.error);
                r = cli.Send("fx.on", new { target = "nothing", index = 1, on = true }).GetAwaiter().GetResult();
                check(!r.ok && r.error.Length > 0, "不正な命令は失敗として返る");
                server.Publish(new { recording = false, paused = false, standby = false, inputs = Array.Empty<object>(), outputs = Array.Empty<object>() });
                var until = DateTime.UtcNow.AddSeconds(5);
                while (got != null && got.Recording && DateTime.UtcNow < until) Thread.Sleep(20);
                check(got != null && !got.Recording, "状態の変化の通知が届く");
            }
            catch (Exception ex)
            {
                check(false, "制御APIのクライアント", ex.GetType().Name + ": " + ex.Message);
            }
            cli.Disconnect();
            check(!cli.Connected, "切断できる");
            var lost = new ControlClient("GokkoNone." + Guid.NewGuid().ToString("N"));
            check(!lost.ConnectAsync(200).GetAwaiter().GetResult(), "相手がいなければ接続は失敗する（例外にならない）");
            check(!lost.Send("hello").GetAwaiter().GetResult().ok, "未接続の送信は失敗を返す");
        }
        section("Gokko4DJ: 文言（M18）");
        {
            var root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "gokko", "Lang", "ja.ini"))) root = Path.GetDirectoryName(root);
            if (root == null) { check(false, "gokko/Lang/ja.ini が見つからない"); return; }
            var ini = File.ReadAllText(Path.Combine(root, "gokko", "Lang", "ja.ini"));
            Loc.Load(ini);
            var dup = ini.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != ';' && l.Contains('='))
                .Select(l => l[..l.IndexOf('=')].Trim().ToLowerInvariant()).GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            check(dup.Count == 0, "Gokko4DJ の ja.ini に重複キーがない", string.Join(", ", dup));
            var missing = new List<string>();
            var code = new Regex(@"Loc\.(?:Get|Has)\(""([\w.]+)""");
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "gokko"), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                foreach (Match m in code.Matches(File.ReadAllText(f))) if (!Loc.Has(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
            }
            check(missing.Count == 0, "画面が使う文言のキーが全部ある", string.Join(", ", missing.Distinct()));
        }
    }
}
