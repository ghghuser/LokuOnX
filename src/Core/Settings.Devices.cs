using System.Globalization;

namespace Recorder.Core;

/// <summary>録音に使うデバイス1台の設定（Key とゲインdB）。</summary>
public sealed record InputSetting(string Key, double GainDb, bool Enabled = true);

/// <summary>Settings のうち、複数デバイス同時録音に関する部分（PROJECT.md §3-1, §3-2）。</summary>
public sealed partial class Settings
{
    public const int MaxInputs = 4;
    public const double MinGainDb = -30;
    public const double MaxGainDb = 20;

    private static string Suffix(int i) => i == 1 ? "" : i.ToString(CultureInfo.InvariantCulture);

    /// <summary>録音に使うデバイス（上から順）。ini の Device / Gain（2台目以降は Device2 / Gain2 …）。1台も無ければ空。</summary>
    public IReadOnlyList<InputSetting> Inputs
    {
        get
        {
            var list = new List<InputSetting>();
            for (int i = 1; i <= MaxInputs; i++)
            {
                var raw = Get("Device" + Suffix(i), "?");
                if (raw == "?" || raw.Length == 0) break;
                var key = raw == "-" ? "" : raw;   // "-" = 「なし」を選んだ行
                double.TryParse(Get("Gain" + Suffix(i), "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var db);
                var enabled = Get("Enabled" + Suffix(i), "1") != "0";
                list.Add(new InputSetting(key, Math.Clamp(db, MinGainDb, MaxGainDb), enabled));
            }
            return list;
        }
        set
        {
            for (int i = 1; i <= MaxInputs; i++)
            {
                if (i <= value.Count)
                {
                    _ini.Set(Section, "Device" + Suffix(i), value[i - 1].Key.Length == 0 ? "-" : value[i - 1].Key);
                    _ini.Set(Section, "Enabled" + Suffix(i), value[i - 1].Enabled ? "1" : "0");
                    _ini.Set(Section, "Gain" + Suffix(i),
                        Math.Clamp(value[i - 1].GainDb, MinGainDb, MaxGainDb).ToString("0.#", CultureInfo.InvariantCulture));
                }
                else
                {
                    _ini.Remove(Section, "Device" + Suffix(i));
                    _ini.Remove(Section, "Gain" + Suffix(i));
                    _ini.Remove(Section, "Enabled" + Suffix(i));
                }
            }
            Save();
        }
    }

    /// <summary>複数デバイスのとき、デバイスごとに別ファイルにするか。false（既定）＝ミックスして1ファイル。</summary>
    public bool SeparateFiles
    {
        get => Get("SeparateFiles", "0") == "1";
        set => Set("SeparateFiles", value ? "1" : "0");
    }

    /// <summary>デバイスが外れたとき、戻ったら自動で再開するか（既定ON）。OFFなら録音を止める。UIは M7。</summary>
    public bool AutoResume => Get("AutoResume", "1") != "0";
}
