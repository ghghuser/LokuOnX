namespace Recorder.Core;

/// <summary>
/// Settings のうち、M7 のオプション機能（PROJECT.md §3-8）。いずれも既定OFF（トレイのアイコン表示だけ既定ON）。
/// 無音検出・ホットキー・予約録音・デバイス待機/復帰分割・トレイ。プロファイルには含めない。
/// </summary>
public sealed partial class Settings
{
    private bool Flag(string key, bool fallback) => Get(key, fallback ? "1" : "0") == "1";
    private void SetFlag(string key, bool value) => Set(key, value ? "1" : "0");

    private int Number(string key, int fallback, int min, int max) =>
        int.TryParse(Get(key, fallback.ToString()), out var n) ? Math.Clamp(n, min, max) : fallback;

    // ---- 無音検出 ----
    public const int SilenceDbMin = -80, SilenceDbMax = -10;

    /// <summary>無音が続いたら自動で分割する（既定OFF）。</summary>
    public bool SilenceSplit { get => Flag("SilenceSplit", false); set => SetFlag("SilenceSplit", value); }

    /// <summary>無音とみなす音量のしきい値（dB。既定 -50）。</summary>
    public int SilenceDb { get => Number("SilenceDb", -50, SilenceDbMin, SilenceDbMax); set => Set("SilenceDb", value.ToString()); }

    /// <summary>しきい値以下がこの秒数続いたら無音（既定 3 秒）。</summary>
    public int SilenceSeconds { get => Number("SilenceSeconds", 3, 1, 600); set => Set("SilenceSeconds", value.ToString()); }

    /// <summary>音が出たら自動で録音を始める（既定OFF。録音ボタンが「待機」になる）。</summary>
    public bool AutoStart { get => Flag("AutoStart", false); set => SetFlag("AutoStart", value); }

    /// <summary>自動で録音を始める音量のしきい値（dB。既定 -40）。</summary>
    public int AutoStartDb { get => Number("AutoStartDb", -40, SilenceDbMin, SilenceDbMax); set => Set("AutoStartDb", value.ToString()); }

    /// <summary>待機中もレベル表示（旧「入力チェック」）: 録音していないあいだも、メーターと波形を動かす（既定ON）。</summary>
    public bool InputCheck { get => Flag("InputCheck", true); set => SetFlag("InputCheck", value); }

    /// <summary>全体ゲイン（dB。ミックス後に掛ける。最小＝ミュート）。既定 0。デバイスごとのゲインとは別。</summary>
    public double MasterGainDb
    {
        get => double.TryParse(Get("MasterGain", "0"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, MinGainDb, MaxGainDb) : 0;
        set => Set("MasterGain", Math.Clamp(value, MinGainDb, MaxGainDb).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- デバイスが外れたとき（M7。自動再開は AutoResume） ----
    /// <summary>すべてのデバイスが外れたら、戻るまで一時停止する（既定OFF＝無音で埋めて続ける）。</summary>
    public bool WaitForDevice { get => Flag("WaitForDevice", false); set => SetFlag("WaitForDevice", value); }

    /// <summary>デバイスが戻ったら別ファイルに分割する（既定OFF）。</summary>
    public bool SplitOnReturn { get => Flag("SplitOnReturn", false); set => SetFlag("SplitOnReturn", value); }

    // ---- ASIO（M8） ----
    /// <summary>入力デバイスの一覧にASIOを表示する（既定OFF）。ONでも、ASIOドライバが入っていなければ何も増えない。プロファイルには含めない。</summary>
    public bool AsioEnabled { get => Flag("AsioEnabled", false); set => SetFlag("AsioEnabled", value); }

    // ---- ホットキー ----
    public bool HotkeysOn { get => Flag("HotkeysOn", false); set => SetFlag("HotkeysOn", value); }
    public const string DefaultHotkeyStart = "Ctrl+Alt+F9", DefaultHotkeyPause = "Ctrl+Alt+F10", DefaultHotkeySplit = "Ctrl+Alt+F11";
    public string HotkeyStart { get => Get("HotkeyStart", DefaultHotkeyStart); set => Set("HotkeyStart", value); }
    public string HotkeyPause { get => Get("HotkeyPause", DefaultHotkeyPause); set => Set("HotkeyPause", value); }
    public string HotkeySplit { get => Get("HotkeySplit", DefaultHotkeySplit); set => Set("HotkeySplit", value); }

    // ---- 予約録音 ----
    public bool ScheduleOn { get => Flag("ScheduleOn", false); set => SetFlag("ScheduleOn", value); }

    /// <summary>開始する時刻（1日の中の秒。0〜86399）。</summary>
    public int ScheduleTime { get => Number("ScheduleTime", 0, 0, ClockTime.MaxSecondOfDay); set => Set("ScheduleTime", value.ToString()); }

    /// <summary>録音する長さ（秒）。0＝自動では止めない。</summary>
    public int ScheduleSeconds { get => Number("ScheduleSeconds", 3600, 0, SplitTime.Max); set => Set("ScheduleSeconds", value.ToString()); }

    /// <summary>true＝毎日くり返す。false＝次にその時刻になったとき1回だけ（実行後は自動でOFF）。</summary>
    public bool ScheduleDaily { get => Flag("ScheduleDaily", false); set => SetFlag("ScheduleDaily", value); }

    // ---- タスクトレイ ----
    /// <summary>タスクトレイにアイコンを常に表示する（既定ON）。</summary>
    public bool ShowTray { get => Flag("ShowTray", true); set => SetFlag("ShowTray", value); }

    /// <summary>最小化したらタスクバーではなくトレイへ格納する（既定OFF＝タスクバーに残す）。</summary>
    public bool MinimizeToTray { get => Flag("MinimizeToTray", false); set => SetFlag("MinimizeToTray", value); }
}
