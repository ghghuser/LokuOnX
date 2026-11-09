using System.Globalization;

namespace Recorder.Core;

/// <summary>
/// IniFile の1セクションを、型つきで読み書きするための薄い窓口（エフェクト設定の WriteTo / ReadFrom が使う）。
/// 数値はカルチャに依存しない書式。コメント引数は LokuOnX の ini がコメントを持たないため受け取るだけで書かない。
/// </summary>
public sealed class IniSection
{
    private readonly IniFile _ini;
    private readonly string _name;

    public IniSection(IniFile ini, string name) { _ini = ini; _name = name; }

    public void Set(string key, string value, string? comment = null) => _ini.Set(_name, key, value);
    public void Set(string key, int value, string? comment = null) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string key, double value, string? comment = null) => Set(key, value.ToString("0.###", CultureInfo.InvariantCulture));
    public void Set(string key, bool value, string? comment = null) => Set(key, value ? "true" : "false");

    public string GetString(string key, string def = "") => _ini.Get(_name, key, def);

    public int GetInt(string key, int def) =>
        int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    public double GetDouble(string key, double def) =>
        double.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    public bool GetBool(string key, bool def)
    {
        var s = GetString(key);
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return def;
    }
}
