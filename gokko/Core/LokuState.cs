using System.Text.Json;

namespace Gokko.Core;

/// <summary>LokuOnX の制御API（state・通知）から読んだ状態。画面に出す分だけ。</summary>
public sealed class LokuState
{
    public bool Recording { get; init; }
    public bool Paused { get; init; }
    public bool Standby { get; init; }
    public int? Track { get; init; }
    public IReadOnlyList<LokuInput> Inputs { get; init; } = Array.Empty<LokuInput>();
    public IReadOnlyList<LokuOutput> Outputs { get; init; } = Array.Empty<LokuOutput>();

    public static LokuState Parse(JsonElement e)
    {
        bool B(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        double D(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        int I(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
        (bool any, bool on) Fx(JsonElement o) =>
            o.TryGetProperty("fx", out var f) && f.ValueKind == JsonValueKind.Object ? (B(f, "any"), B(f, "on")) : (false, true);

        var inputs = new List<LokuInput>();
        if (e.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
            foreach (var x in ins.EnumerateArray())
            {
                var (any, on) = Fx(x);
                inputs.Add(new LokuInput(I(x, "index"), B(x, "active"), D(x, "gainDb"), B(x, "muted"), any, on));
            }
        var outputs = new List<LokuOutput>();
        if (e.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
            foreach (var x in outs.EnumerateArray())
                outputs.Add(new LokuOutput(I(x, "index"), x.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "", B(x, "on"), D(x, "volumeDb")));
        int? track = e.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var tn) ? tn : null;
        return new LokuState
        {
            Recording = B(e, "recording"),
            Paused = B(e, "paused"),
            Standby = B(e, "standby"),
            Track = track,
            Inputs = inputs,
            Outputs = outputs,
        };
    }
}

public sealed record LokuInput(int Index, bool Active, double GainDb, bool Muted, bool FxAny, bool FxOn);
public sealed record LokuOutput(int Index, string Name, bool On, double VolumeDb);
