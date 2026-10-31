using System.Text;

namespace Recorder.Core;

/// <summary>単純な ini（[セクション] と キー=値）の読み書き。UTF-8。保存は一時ファイル→置換で破損を防ぐ（PROJECT.md §5-15）。</summary>
public sealed class IniFile
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();

    public bool Existed { get; }

    public IniFile(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        Existed = true;
        try
        {
            string section = "";
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[^1] == ']') { section = line[1..^1]; continue; }
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                Put(section, line[..i].Trim(), line[(i + 1)..].Trim());
            }
        }
        catch (IOException) { }
    }

    private static string Id(string section, string key) => section + "\n" + key;

    private void Put(string section, string key, string value)
    {
        var id = Id(section, key);
        if (!_values.ContainsKey(id)) _order.Add(id);
        _values[id] = value;
    }

    public string Get(string section, string key, string fallback) =>
        _values.TryGetValue(Id(section, key), out var v) ? v : fallback;

    public void Set(string section, string key, string value) => Put(section, key, value);

    public void Remove(string section, string key)
    {
        var id = Id(section, key);
        if (_values.Remove(id)) _order.Remove(id);
    }

    /// <summary>保存。失敗したら例外を投げる（呼び出し側で扱う）。</summary>
    public void Save()
    {
        var sb = new StringBuilder();
        string? current = null;
        foreach (var id in _order)
        {
            var parts = id.Split('\n', 2);
            if (parts[0] != current)
            {
                if (current != null) sb.AppendLine();
                sb.AppendLine($"[{parts[0]}]");
                current = parts[0];
            }
            sb.AppendLine($"{parts[1]}={_values[id]}");
        }
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, _path, overwrite: true);
    }
}
