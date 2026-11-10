using System.Globalization;

namespace Recorder.Core;

/// <summary>
/// Settings のうち、出力デバイス（M11-2。いまは1台。M11-3 で最大4台・プリセット対応）。
/// 「出力する」のON/OFFは保存しない（起動のたびに停止から始める。意図しない音が出ないように）。
/// </summary>
public sealed partial class Settings
{
    /// <summary>出力先（"default"＝既定の出力デバイス / デバイスID / 空＝未選択）。</summary>
    public string OutputDevice { get => Get("OutputDevice", ""); set => Set("OutputDevice", value); }

    /// <summary>出力の全体音量（dB。最小＝ミュート）。既定 0。</summary>
    public double OutputGainDb
    {
        get => double.TryParse(Get("OutputGain", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? Math.Clamp(d, MinGainDb, MaxGainDb) : 0;
        set => Set("OutputGain", Math.Clamp(value, MinGainDb, MaxGainDb).ToString("0.#", CultureInfo.InvariantCulture));
    }
}
