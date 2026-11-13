using System.Globalization;

namespace Recorder.Core;

/// <summary>
/// Settings のうち、出力（M11-3。最大4枚）と入力の名前。
/// 「出力する」のON/OFFは保存しない（起動のたびに停止から始める。意図しない音が出ないように）。
/// ini: [Outputs] Count / Name1 / Device1 / Volume1 / Send1_1（入力1→出力1。"1,0"＝オン・0dB／"0,0"＝オフ）/ Fx1_EqOn …（出力1の最終ミックスのエフェクト。M11-5。初期値のままなら書かない）。入力の名前は本体の InputName1〜4。
/// 初期値のままの項目は書かない。11-2 の OutputDevice / OutputGain（出力1台）は、Count が無いときだけ読み込んで引き継ぐ。
/// </summary>
public sealed partial class Settings
{
    private const string OutSection = "Outputs";

    /// <summary>出力カード（1〜4枚。最初は「ミックス1」の1枚）。</summary>
    public IReadOnlyList<OutputConfig> Outputs
    {
        get
        {
            var list = new List<OutputConfig>();
            if (!int.TryParse(_ini.Get(OutSection, "Count", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                // 11-2 までの保存（1台）を引き継ぐ
                var old = new OutputConfig(OutputConfig.DefaultName(0), Get("OutputDevice", ""),
                    Num(Get("OutputGain", "0")), Enumerable.Repeat(new OutSend(), OutputConfig.InputSlots).ToList());
                list.Add(old.Normalize(0));
                return list;
            }
            count = Math.Clamp(count, 1, OutputConfig.MaxOutputs);
            for (int i = 1; i <= count; i++)
            {
                var sends = new List<OutSend>();
                for (int s = 1; s <= OutputConfig.InputSlots; s++)
                {
                    var raw = _ini.Get(OutSection, $"Send{i}_{s}", "1,0").Split(',');
                    bool on = raw.Length < 1 || raw[0].Trim() != "0";
                    double db = raw.Length > 1 ? Num(raw[1]) : 0;
                    sends.Add(new OutSend(on, db));
                }
                var fx = OutputFxSetting.ReadFrom(new IniSection(_ini, OutSection), $"Fx{i}_");
                list.Add(new OutputConfig(_ini.Get(OutSection, $"Name{i}", ""), _ini.Get(OutSection, $"Device{i}", ""),
                    Num(_ini.Get(OutSection, $"Volume{i}", "0")), sends, fx).Normalize(i - 1));
            }
            return list;
        }
        set
        {
            var list = value.Take(OutputConfig.MaxOutputs).Select((c, i) => c.Normalize(i)).ToList();
            if (list.Count == 0) list.Add(OutputConfig.Default(0));
            _ini.Set(OutSection, "Count", list.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 1; i <= OutputConfig.MaxOutputs; i++)
            {
                if (i > list.Count)
                {
                    _ini.Remove(OutSection, $"Name{i}");
                    _ini.Remove(OutSection, $"Device{i}");
                    _ini.Remove(OutSection, $"Volume{i}");
                    for (int s = 1; s <= OutputConfig.InputSlots; s++) _ini.Remove(OutSection, $"Send{i}_{s}");
                    foreach (var k in OutputFxSetting.IniKeys) _ini.Remove(OutSection, $"Fx{i}_{k}");
                    continue;
                }
                var c = list[i - 1];
                _ini.Set(OutSection, $"Name{i}", c.Name);
                _ini.Set(OutSection, $"Device{i}", c.DeviceId);
                _ini.Set(OutSection, $"Volume{i}", c.VolumeDb.ToString("0.#", CultureInfo.InvariantCulture));
                for (int s = 1; s <= OutputConfig.InputSlots; s++)
                {
                    var send = c.SendAt(s - 1);
                    var key = $"Send{i}_{s}";
                    if (send.On && send.Db == 0) _ini.Remove(OutSection, key);
                    else _ini.Set(OutSection, key, (send.On ? "1," : "0,") + send.Db.ToString("0.#", CultureInfo.InvariantCulture));
                }
                // 出力ごとのエフェクト（初期値のままなら書かず、前に書いたものも消す）
                foreach (var k in OutputFxSetting.IniKeys) _ini.Remove(OutSection, $"Fx{i}_{k}");
                if (c.Fx is { IsDefault: false }) c.Fx.WriteTo(new IniSection(_ini, OutSection), $"Fx{i}_", false);
            }
            _ini.Remove(Section, "OutputDevice");   // 11-2 の旧キー（Count を書いたので、もう読まれない）
            _ini.Remove(Section, "OutputGain");
            Save();
        }
    }

    private static double Num(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>入力の名前（入力1〜4。空＝既定の「入力N」）。メイン画面・出力カードで共通に使う。プロファイルに含む。</summary>
    public IReadOnlyList<string> InputNames
    {
        get => Enumerable.Range(1, OutputConfig.InputSlots)
            .Select(i => OutputConfig.CleanName(Get("InputName" + i, ""), "")).ToList();
        set
        {
            for (int i = 1; i <= OutputConfig.InputSlots; i++)
            {
                var v = i <= value.Count ? OutputConfig.CleanName(value[i - 1], "") : "";
                if (v.Length == 0) _ini.Remove(Section, "InputName" + i);
                else _ini.Set(Section, "InputName" + i, v);
            }
            Save();
        }
    }

    /// <summary>出力カードの「入力」欄を畳んでいるか（出力ごとに記憶）。</summary>
    public bool OutputFolded(int index) => _ini.Get(OutSection, $"Fold{index + 1}", "0") == "1";

    public void SetOutputFolded(int index, bool folded)
    {
        if (folded) _ini.Set(OutSection, $"Fold{index + 1}", "1");
        else _ini.Remove(OutSection, $"Fold{index + 1}");
        Save();
    }

    /// <summary>出力ウィンドウの位置と大きさ（"左,上,幅,高さ"。空＝未保存）。</summary>
    public string OutputWindowRect
    {
        get => _ini.Get(OutSection, "WindowRect", "");
        set { _ini.Set(OutSection, "WindowRect", value); Save(); }
    }
}
