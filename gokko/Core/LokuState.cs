using System.Text.Json;

namespace Gokko.Core;

public sealed record LokuInput(int Index, bool Active, double GainDb, bool Muted, bool FxAny, bool FxOn);

public sealed record LokuOutput(int Index, string Name, bool On, bool Running, double VolumeDb, bool FxAny, bool FxOn);

/// <summary>LokuOnX の制御API（state・通知）から読んだ状態。</summary>
public sealed record LokuState(
    bool Recording, bool Paused, bool Standby, int? Track, double MasterGainDb,
    IReadOnlyList<LokuInput> Inputs, IReadOnlyList<LokuOutput> Outputs)
{
    public static LokuState Empty { get; } = new(false, false, false, null, 0, Array.Empty<LokuInput>(), Array.Empty<LokuOutput>());

    /// <summary>読めない項目は初期値で補う（相手の版が違っても落ちない）。</summary>
    public static LokuState Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return Empty;
        var inputs = new List<LokuInput>();
        if (e.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
            foreach (var i in ins.EnumerateArray())
            {
                if (i.ValueKind != JsonValueKind.Object) continue;
                var (any, on) = Fx(i);
                inputs.Add(new LokuInput(Int(i, "index", 0), Bool(i, "active", false), Dbl(i, "gainDb", 0), Bool(i, "muted", false), any, on));
            }
        var outputs = new List<LokuOutput>();
        if (e.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
            foreach (var o in outs.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object) continue;
                var (any, on) = Fx(o);
                outputs.Add(new LokuOutput(Int(o, "index", 0), Str(o, "name"), Bool(o, "on", false), Bool(o, "running", false), Dbl(o, "volumeDb", 0), any, on));
            }
        int? track = e.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var tn) ? tn : null;
        return new LokuState(Bool(e, "recording", false), Bool(e, "paused", false), Bool(e, "standby", false), track, Dbl(e, "masterGainDb", 0), inputs, outputs);
    }

    private static (bool Any, bool On) Fx(JsonElement e) =>
        e.TryGetProperty("fx", out var fx) && fx.ValueKind == JsonValueKind.Object ? (Bool(fx, "any", false), Bool(fx, "on", true)) : (false, true);

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False) ? p.GetBoolean() : fallback;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : fallback;

    private static double Dbl(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) && double.IsFinite(d) ? d : fallback;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
