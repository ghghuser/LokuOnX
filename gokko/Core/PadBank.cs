using System.Globalization;
using Recorder.Core;

namespace Gokko.Core;

/// <summary>パッドの一覧（最大10×10＝100個）。番号 id＝行×10＋列（0〜99）。画面に出す数（横・縦）を減らしても、外れたパッドの設定は残る。設定は ini の [Pads] に、使っているパッドだけ書く。</summary>
public sealed class PadBank
{
    public const int Side = 10;
    public const int Total = Side * Side;
    private const string Section = "Pads";

    private readonly PadConfig[] _pads = new PadConfig[Total];

    public PadBank()
    {
        for (var i = 0; i < Total; i++) _pads[i] = PadConfig.Empty;
    }

    public static int Id(int row, int col) => row * Side + col;

    /// <summary>2つのパッドの中身を入れ替える。</summary>
    public void Swap(int a, int b)
    {
        (_pads[a], _pads[b]) = (_pads[b], _pads[a]);
    }

    public PadConfig this[int id]
    {
        get => _pads[id];
        set => _pads[id] = value.Normalize();
    }

    private static string Key(int id, string field) => $"P{id + 1}{field}";

    public void Load(IniFile ini)
    {
        for (var id = 0; id < Total; id++)
        {
            var file = ini.Get(Section, Key(id, "File"), "");
            if (file.Length == 0) { _pads[id] = PadConfig.Empty; continue; }
            var mode = ini.Get(Section, Key(id, "Mode"), "once").ToLowerInvariant() switch
            {
                "loop" => PadMode.Loop,
                "hold" => PadMode.Hold,
                _ => PadMode.Once,
            };
            double.TryParse(ini.Get(Section, Key(id, "Db"), "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var db);
            int.TryParse(ini.Get(Section, Key(id, "Color"), "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var color);
            _pads[id] = new PadConfig(ini.Get(Section, Key(id, "Name"), ""), file, mode, db, color).Normalize();
        }
    }

    public void Save(IniFile ini)
    {
        for (var id = 0; id < Total; id++)
        {
            foreach (var f in new[] { "Name", "File", "Mode", "Db", "Color" }) ini.Remove(Section, Key(id, f));
            var p = _pads[id];
            if (p.IsEmpty) continue;
            ini.Set(Section, Key(id, "Name"), p.Name);
            ini.Set(Section, Key(id, "File"), p.FilePath);
            ini.Set(Section, Key(id, "Mode"), p.Mode.ToString().ToLowerInvariant());
            ini.Set(Section, Key(id, "Db"), p.VolumeDb.ToString("0.0", CultureInfo.InvariantCulture));
            ini.Set(Section, Key(id, "Color"), p.Color.ToString(CultureInfo.InvariantCulture));
        }
    }
}
