using System.Globalization;
using Recorder.Core;

namespace Gokko.Core;

/// <summary>Gokko4DJ の設定（gokko.ini）。パッドは <see cref="PadBank"/> が同じ ini に書く。</summary>
public sealed class GokkoSettings
{
    public const string FileName = "gokko.ini";
    private const string Section = "Gokko";

    private readonly IniFile _ini;

    public GokkoSettings(string path)
    {
        _ini = new IniFile(path);
        OutputDeviceId = _ini.Get(Section, "OutputDevice", "");
        double.TryParse(_ini.Get(Section, "MasterDb", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var m);
        MasterDb = double.IsFinite(m) ? Math.Clamp(m, PadConfig.MinDb, PadConfig.MaxDb) : 0;
        Cols = ReadSize("Cols");
        Rows = ReadSize("Rows");
        Theme = _ini.Get(Section, "Theme", "auto");
        if (Theme != "light" && Theme != "dark") Theme = "auto";
        for (var r = 0; r < PadBank.Side; r++)
            for (var c = 0; c < PadBank.Side; c++)
                foreach (var k in new[] { BorderKey(true, r, c), BorderKey(false, r, c) })
                    if (int.TryParse(_ini.Get("Borders", k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci) && ci >= 0 && ci < PadConfig.ColorCount)
                        Borders[k] = ci;
        var apps = new List<string>();
        for (var i = 1; i <= LauncherList.Max; i++) apps.Add(_ini.Get("Launcher", "App" + i, ""));
        Launcher = LauncherList.Clean(apps);
        Bank.Load(_ini);
    }

    private int ReadSize(string key)
    {
        int.TryParse(_ini.Get(Section, key, "4"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
        return Math.Clamp(n, 1, PadBank.Side);
    }

    /// <summary>枠（タイルの間の線）のキー。縦線＝タイル(r,c)の右、横線＝タイル(r,c)の下。</summary>
    public static string BorderKey(bool vertical, int row, int col) => $"{(vertical ? "V" : "H")}{row}_{col}";

    public IniFile Ini => _ini;
    public PadBank Bank { get; } = new();

    /// <summary>出力デバイスの ID。空＝Windows の既定の出力。</summary>
    public string OutputDeviceId { get; set; }
    public double MasterDb { get; set; }
    /// <summary>画面に出すタイルの数（横・縦。1〜10）。</summary>
    public int Cols { get; set; }
    public int Rows { get; set; }
    /// <summary>画面の色: auto／light／dark。</summary>
    public string Theme { get; set; }
    /// <summary>色を付けた枠（キー → 色の番号）。</summary>
    public Dictionary<string, int> Borders { get; } = new();
    public List<string> Launcher { get; set; }

    public void Save()
    {
        _ini.Set(Section, "OutputDevice", OutputDeviceId);
        _ini.Set(Section, "MasterDb", MasterDb.ToString("0.0", CultureInfo.InvariantCulture));
        _ini.Set(Section, "Cols", Cols.ToString(CultureInfo.InvariantCulture));
        _ini.Set(Section, "Rows", Rows.ToString(CultureInfo.InvariantCulture));
        _ini.Set(Section, "Theme", Theme);
        _ini.Remove(Section, "Page");
        for (var r = 0; r < PadBank.Side; r++)
            for (var c = 0; c < PadBank.Side; c++)
                foreach (var k in new[] { BorderKey(true, r, c), BorderKey(false, r, c) })
                {
                    if (Borders.TryGetValue(k, out var ci)) _ini.Set("Borders", k, ci.ToString(CultureInfo.InvariantCulture));
                    else _ini.Remove("Borders", k);
                }
        var list = LauncherList.Clean(Launcher);
        for (var i = 1; i <= LauncherList.Max; i++)
        {
            if (i <= list.Count) _ini.Set("Launcher", "App" + i, list[i - 1]);
            else _ini.Remove("Launcher", "App" + i);
        }
        Bank.Save(_ini);
        _ini.Save();
    }
}
