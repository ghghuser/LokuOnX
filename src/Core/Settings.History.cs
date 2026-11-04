namespace Recorder.Core;

/// <summary>
/// Settings のうち、録音履歴（録音して保存したファイルのパス）と、最近使った保存先フォルダ（PROJECT.md §3-7）。
/// 履歴は「空にする」で消すまで残る（ファイル自体は消さない）。ini の [History]（Count, P0…）と [RecentFolders]（Count, F0…）。
/// </summary>
public sealed partial class Settings
{
    public const int MaxHistory = 1000;
    public const int MaxRecentFolders = 10;

    private List<string> ReadList(string section, string key)
    {
        int.TryParse(_ini.Get(section, "Count", "0"), out var count);
        var list = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var v = _ini.Get(section, key + i, "");
            if (v.Length > 0) list.Add(v);
        }
        return list;
    }

    private void WriteList(string section, string key, IReadOnlyList<string> list)
    {
        int.TryParse(_ini.Get(section, "Count", "0"), out var old);
        for (var i = 0; i < list.Count; i++) _ini.Set(section, key + i, list[i]);
        for (var i = list.Count; i < old; i++) _ini.Remove(section, key + i);
        _ini.Set(section, "Count", list.Count.ToString());
        Save();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- 録音履歴 ----
    public IReadOnlyList<string> History => ReadList("History", "P");

    public void AddHistory(string path)
    {
        var list = ReadList("History", "P");
        list.RemoveAll(p => Same(p, path));
        list.Add(path);
        if (list.Count > MaxHistory) list.RemoveRange(0, list.Count - MaxHistory);
        WriteList("History", "P", list);
    }

    public void RemoveFromHistory(IEnumerable<string> paths)
    {
        var set = paths.ToList();
        var list = ReadList("History", "P");
        if (list.RemoveAll(p => set.Any(q => Same(p, q))) > 0) WriteList("History", "P", list);
    }

    public void ReplaceInHistory(string oldPath, string newPath)
    {
        var list = ReadList("History", "P");
        var i = list.FindIndex(p => Same(p, oldPath));
        if (i < 0) return;
        list[i] = newPath;
        WriteList("History", "P", list);
    }

    public void ClearHistory() => WriteList("History", "P", new List<string>());

    // ---- 最近使った保存先（新しい順。最大10） ----
    public IReadOnlyList<string> RecentFolders => ReadList("RecentFolders", "F");

    public void AddRecentFolder(string folder)
    {
        folder = folder.Trim().TrimEnd('\\', '/');
        if (folder.Length == 0) return;
        var list = ReadList("RecentFolders", "F");
        if (list.Count > 0 && Same(list[0], folder)) return;
        list.RemoveAll(p => Same(p, folder));
        list.Insert(0, folder);
        if (list.Count > MaxRecentFolders) list.RemoveRange(MaxRecentFolders, list.Count - MaxRecentFolders);
        WriteList("RecentFolders", "F", list);
    }
}
