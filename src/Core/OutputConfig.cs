namespace Recorder.Core;

/// <summary>入力1つから、ある出力への送り（M11-3）。On＝この出力へ送る、Db＝入力×出力ごとの音量（最小＝ミュート）。</summary>
public sealed record OutSend(bool On = true, double Db = 0);

/// <summary>
/// 出力カード1枚ぶんの設定（M11-3。名前・出力先・全体音量・入力ごとの送り）。「出力する」のON/OFFは含めない（起動のたびにオフ）。
/// 送りは入力の行（スロット1〜4。メイン画面の入力デバイス欄の上から順）ごとに持つ。
/// Fx＝この出力の最終ミックスにかけるエフェクト（M11-5。null＝かけない）。record の with で複製されるので、編集するときは必ず Clone してから。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed record OutputConfig(string Name, string DeviceId, double VolumeDb, IReadOnlyList<OutSend> Sends, OutputFxSetting? Fx = null)
{
    public const int MaxOutputs = 4;
    public const int InputSlots = 4;
    public const int MaxNameLength = 12;

    public static string DefaultName(int index) => $"ミックス{index + 1}";

    public static string DefaultInputName(int slot) => $"入力{slot + 1}";

    /// <summary>入力の表示名（付けた名前があればそれ、なければ「入力N」）。</summary>
    public static string InputLabel(IReadOnlyList<string> names, int slot)
    {
        var n = slot >= 0 && slot < names.Count ? (names[slot] ?? "").Trim() : "";
        return n.Length > 0 ? n : DefaultInputName(slot);
    }

    /// <summary>名前を整える（前後の空白を除き、12文字まで。空なら fallback）。</summary>
    public static string CleanName(string? name, string fallback)
    {
        var t = (name ?? "").Trim();
        if (t.Length > MaxNameLength) t = t.Substring(0, MaxNameLength);
        return t.Length == 0 ? fallback : t;
    }

    public static OutputConfig Default(int index) =>
        new(DefaultName(index), "", 0, Enumerable.Repeat(new OutSend(), InputSlots).ToList());

    /// <summary>この出力のエフェクト（なければ何も掛けない設定。呼び出し側が書き換えてよい複製を返す）。</summary>
    public OutputFxSetting FxCopy() => Fx?.Clone() ?? new OutputFxSetting();

    public OutSend SendAt(int slot) => slot >= 0 && slot < Sends.Count ? Sends[slot] : new OutSend();

    public OutputConfig WithSend(int slot, OutSend send)
    {
        var list = Enumerable.Range(0, InputSlots).Select(SendAt).ToList();
        if (slot >= 0 && slot < InputSlots) list[slot] = send;
        return this with { Sends = list };
    }

    /// <summary>範囲外・壊れた値を範囲内へ直す（読み込み時）。index は何枚目か（名前の既定に使う）。</summary>
    public OutputConfig Normalize(int index)
    {
        var sends = Enumerable.Range(0, InputSlots)
            .Select(i => SendAt(i) with { Db = Math.Clamp(double.IsNaN(SendAt(i).Db) ? 0 : SendAt(i).Db, Settings.MinGainDb, Settings.MaxGainDb) })
            .ToList();
        OutputFxSetting? fx = null;
        if (Fx != null)
        {
            fx = Fx.Clone();
            fx.Normalize();
            if (fx.IsDefault) fx = null;   // 何も設定していなければ持たない（保存・比較が簡単になる）
        }
        return new OutputConfig(CleanName(Name, DefaultName(index)), DeviceId ?? "",
            Math.Clamp(double.IsNaN(VolumeDb) ? 0 : VolumeDb, Settings.MinGainDb, Settings.MaxGainDb), sends, fx);
    }
}
