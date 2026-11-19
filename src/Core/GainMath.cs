namespace Recorder.Core;

/// <summary>左右それぞれのゲイン（dB）。double からそのまま代入できる（左右同じ値）。M15（L/R別ゲイン）。</summary>
public readonly record struct ChGain(double L, double R)
{
    public static implicit operator ChGain(double db) => new(db, db);
}

/// <summary>ゲイン（dB）と倍率の変換。最小値（Settings.MinGainDb）は「ミュート」＝倍率 0。録音・入力チェック・全体ゲインで共通。WPF には依存しない。</summary>
public static class GainMath
{
    public static bool IsMuted(double db) => db <= Settings.MinGainDb;

    public static float DbToLinear(double db) =>
        IsMuted(db) ? 0f : (float)Math.Pow(10.0, Math.Clamp(db, -60.0, 24.0) / 20.0);

    /// <summary>
    /// 入力のゲインの動かし方（M15）から、左右それぞれのゲイン（dB）を求める。
    /// mode 0＝左右一括（db を左右に）／1＝左右それぞれ（db＝左、rDb＝右）／2＝バランス（音量 db は固定で、balance −1（左）〜+1（右）。
    /// 右へずらすと左が下がり、端（±1）でその側がミュート。ずらさない側は db のまま）。
    /// </summary>
    public static ChGain Channels(int mode, double db, double rDb, double balance)
    {
        if (mode == 1) return new ChGain(db, rDb);
        if (mode != 2 || IsMuted(db)) return new ChGain(db, db);
        double b = Math.Clamp(double.IsNaN(balance) ? 0 : balance, -1, 1);
        double down = db - Math.Abs(b) * (db - Settings.MinGainDb);   // 音量 db から最小（ミュート）まで、ずらした分だけ下げる
        if (Math.Abs(b) >= 0.999) down = Settings.MinGainDb;
        return b > 0 ? new ChGain(down, db) : b < 0 ? new ChGain(db, down) : new ChGain(db, db);
    }
}
