namespace Recorder.Core;

/// <summary>Settings のうち、ファイル名テンプレート・日付フォルダ・番号に関する部分（PROJECT.md §3-4）。</summary>
public sealed partial class Settings
{
    private const string TemplatesSection = "Templates";

    /// <summary>テンプレート一覧が ini に無ければ（初回起動・旧版からの更新）、初期テンプレートを登録する。</summary>
    private void EnsureTemplates()
    {
        if (_ini.Get(TemplatesSection, "Count", "") != "") return;
        WriteTemplates(NameTemplates.Defaults);
    }

    /// <summary>登録されているテンプレート（名前を付けて保存したもの）。</summary>
    public IReadOnlyList<NameTemplate> Templates
    {
        get
        {
            int.TryParse(_ini.Get(TemplatesSection, "Count", "0"), out var count);
            var list = new List<NameTemplate>();
            for (int i = 0; i < count; i++)
            {
                var name = _ini.Get(TemplatesSection, $"Name{i}", "");
                if (name.Length == 0) continue;
                list.Add(new NameTemplate(name, _ini.Get(TemplatesSection, $"Pattern{i}", FileNamer.DefaultPattern)));
            }
            return list;
        }
    }

    private void WriteTemplates(IReadOnlyList<NameTemplate> list)
    {
        int.TryParse(_ini.Get(TemplatesSection, "Count", "0"), out var old);
        for (int i = 0; i < list.Count; i++)
        {
            _ini.Set(TemplatesSection, $"Name{i}", list[i].Name);
            _ini.Set(TemplatesSection, $"Pattern{i}", list[i].Pattern);
        }
        for (int i = list.Count; i < old; i++)
        {
            _ini.Remove(TemplatesSection, $"Name{i}");
            _ini.Remove(TemplatesSection, $"Pattern{i}");
        }
        _ini.Set(TemplatesSection, "Count", list.Count.ToString());
        Save();
    }

    /// <summary>同じ名前があれば上書き、なければ追加。</summary>
    public void SaveTemplate(string name, string pattern)
    {
        var list = Templates.ToList();
        var i = list.FindIndex(t => t.Name == name);
        if (i >= 0) list[i] = new NameTemplate(name, pattern); else list.Add(new NameTemplate(name, pattern));
        WriteTemplates(list);
    }

    public void DeleteTemplate(string name) =>
        WriteTemplates(Templates.Where(t => t.Name != name).ToList());

    /// <summary>初期テンプレート（日時形式・トラック形式）を元の内容で登録し直す。ほかのテンプレートは残す。</summary>
    public void RestoreDefaultTemplates()
    {
        var list = Templates.ToList();
        foreach (var d in NameTemplates.Defaults)
        {
            var i = list.FindIndex(t => t.Name == d.Name);
            if (i >= 0) list[i] = d; else list.Add(d);
        }
        WriteTemplates(list);
    }

    /// <summary>最後に選んだテンプレート名。</summary>
    public string Template { get => Get("Template", NameTemplates.DefaultName); set => Set("Template", value); }

    /// <summary>いま使っているファイル名パターン。テンプレートを選ぶと中身がここへ入り、編集もここへ反映される。</summary>
    public string Pattern
    {
        get
        {
            var p = Get("Pattern", "");
            if (p.Length > 0) return p;
            return Templates.FirstOrDefault(t => t.Name == Template)?.Pattern ?? FileNamer.DefaultPattern;
        }
        set => Set("Pattern", value);
    }

    /// <summary>日付フォルダを作るか。既定ON。</summary>
    public bool DateFolder { get => Get("DateFolder", "1") != "0"; set => Set("DateFolder", value ? "1" : "0"); }

    public string DateFolderFormat
    {
        get => Get("DateFolderFormat", FileNamer.DefaultDateFormat);
        set => Set("DateFolderFormat", value);
    }

    /// <summary>{Track} の開始番号。新しい録音ごとにこの番号へ戻る。再起動後も維持。</summary>
    public int TrackStart
    {
        get => int.TryParse(Get("TrackStart", "1"), out var n) && n >= 1 ? n : 1;
        set => Set("TrackStart", value.ToString());
    }
}
