namespace Recorder.Core;

/// <summary>制御API（M17）が操作する先（メイン画面）。画面のスレッドへ渡すのは実装側の仕事。null＝成功、文字列＝失敗の理由。index は 1 から数える（入力1〜4・出力 1〜4＝A〜D）。</summary>
public interface IControlHost
{
    /// <summary>いまの状態（録音・入力・出力・全体ゲイン）。JSON にできる形で返す。</summary>
    Task<object> State();
    Task<string?> RecStart();
    Task<string?> RecStop();
    Task<string?> RecPause(bool pause);
    Task<string?> RecSplit();
    /// <summary>エフェクトのオン・オフ（オフ＝すべてバイパス。設定は残る）。output＝false で入力、true で出力。</summary>
    Task<string?> FxOn(bool output, int index, bool on);
    Task<string?> OutputOn(int index, bool on);
    Task<string?> OutputVolume(int index, double db);
    Task<string?> InputGain(int index, double db);
    Task<string?> InputMute(int index, bool mute);
    Task<string?> MasterGain(double db);
}

/// <summary>制御API（M17）の命令の振り分けと、引数の検査。WPF・Windows API に依存しない。</summary>
public sealed class ControlDispatcher
{
    /// <summary>ゲインの範囲（Settings.MinGainDb／MaxGainDb と同じ）。</summary>
    public const double MinGainDb = -30, MaxGainDb = 20;

    private readonly IControlHost _host;
    private readonly string _app, _version;

    public ControlDispatcher(IControlHost host, string appName, string version)
    {
        _host = host;
        _app = appName;
        _version = version;
    }

    /// <summary>要求を実行して、応答の1行を返す（例外は "internal" の失敗応答にする）。</summary>
    public async Task<string> Handle(ControlRequest r)
    {
        try
        {
            return await Run(r).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return ControlProtocol.Fail(r.Id, "internal", "internal error");
        }
    }

    private async Task<string> Run(ControlRequest r)
    {
        switch (r.Cmd)
        {
            case "hello":
                return ControlProtocol.Ok(r.Id, new { app = _app, version = _version, api = ControlProtocol.Version });
            case "state":
                return ControlProtocol.Ok(r.Id, await _host.State().ConfigureAwait(false));
            case "rec.start": return Done(r, await _host.RecStart().ConfigureAwait(false));
            case "rec.stop": return Done(r, await _host.RecStop().ConfigureAwait(false));
            case "rec.pause": return Done(r, await _host.RecPause(true).ConfigureAwait(false));
            case "rec.resume": return Done(r, await _host.RecPause(false).ConfigureAwait(false));
            case "rec.split": return Done(r, await _host.RecSplit().ConfigureAwait(false));
            case "fx.on":
            {
                if (!r.TryGetString("target", out var target) || (target != "input" && target != "output")) return Bad(r, "target must be \"input\" or \"output\"");
                if (!Index(r, out var i, out var bad)) return bad;
                if (!r.TryGetBool("on", out var on)) return Bad(r, "on (true/false) is required");
                return Done(r, await _host.FxOn(target == "output", i, on).ConfigureAwait(false));
            }
            case "output.on":
            {
                if (!Index(r, out var i, out var bad)) return bad;
                if (!r.TryGetBool("on", out var on)) return Bad(r, "on (true/false) is required");
                return Done(r, await _host.OutputOn(i, on).ConfigureAwait(false));
            }
            case "output.volume":
            {
                if (!Index(r, out var i, out var bad)) return bad;
                if (!Gain(r, out var db, out bad)) return bad;
                return Done(r, await _host.OutputVolume(i, db).ConfigureAwait(false));
            }
            case "input.gain":
            {
                if (!Index(r, out var i, out var bad)) return bad;
                if (!Gain(r, out var db, out bad)) return bad;
                return Done(r, await _host.InputGain(i, db).ConfigureAwait(false));
            }
            case "input.mute":
            {
                if (!Index(r, out var i, out var bad)) return bad;
                if (!r.TryGetBool("mute", out var mute)) return Bad(r, "mute (true/false) is required");
                return Done(r, await _host.InputMute(i, mute).ConfigureAwait(false));
            }
            case "master.gain":
            {
                if (!Gain(r, out var db, out var bad)) return bad;
                return Done(r, await _host.MasterGain(db).ConfigureAwait(false));
            }
            default:
                return ControlProtocol.Fail(r.Id, "unknown_cmd", "unknown command: " + r.Cmd);
        }
    }

    private static string Done(ControlRequest r, string? error) =>
        error == null ? ControlProtocol.Ok(r.Id) : ControlProtocol.Fail(r.Id, "failed", error);

    private static string Bad(ControlRequest r, string message) => ControlProtocol.Fail(r.Id, "bad_request", message);

    private static bool Index(ControlRequest r, out int zeroBased, out string reply)
    {
        zeroBased = 0;
        reply = "";
        if (!r.TryGetInt("index", out var n) || n < 1 || n > OutputConfig.InputSlots)
        {
            reply = Bad(r, $"index (1-{OutputConfig.InputSlots}) is required");
            return false;
        }
        zeroBased = n - 1;
        return true;
    }

    private static bool Gain(ControlRequest r, out double db, out string reply)
    {
        reply = "";
        if (!r.TryGetDouble("db", out db) || db < MinGainDb || db > MaxGainDb)
        {
            reply = Bad(r, $"db ({MinGainDb} to {MaxGainDb}) is required");
            return false;
        }
        return true;
    }
}
