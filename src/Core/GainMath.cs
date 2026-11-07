namespace Recorder.Core;

/// <summary>ゲイン（dB）と倍率の変換。最小値（Settings.MinGainDb）は「ミュート」＝倍率 0。録音・入力チェック・全体ゲインで共通。WPF には依存しない。</summary>
public static class GainMath
{
    public static bool IsMuted(double db) => db <= Settings.MinGainDb;

    public static float DbToLinear(double db) =>
        IsMuted(db) ? 0f : (float)Math.Pow(10.0, Math.Clamp(db, -60.0, 24.0) / 20.0);
}
