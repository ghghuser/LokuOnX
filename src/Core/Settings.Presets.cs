namespace Recorder.Core;

/// <summary>Settings のうち、プリセットに関する部分（PROJECT.md §3-6）。ini の [Presets]（Count, Name&lt;i&gt;, Json&lt;i&gt;）に保存。</summary>
public sealed partial class Settings
{
    private const string PresetSection = "Presets";

    /// <summary>保存されているプリセット。壊れた JSON のものは飛ばす。</summary>
    public IReadOnlyList<PresetData> Presets
    {
        get
        {
            int.TryParse(_ini.Get(PresetSection, "Count", "0"), out var count);
            var list = new List<PresetData>();
            for (int i = 0; i < count; i++)
            {
                var json = _ini.Get(PresetSection, $"Json{i}", "");
                if (json.Length == 0) continue;
                var parsed = PresetJson.Parse(json, out _);
                if (parsed is { Count: > 0 }) list.Add(parsed[0]);
            }
            return list;
        }
    }

    private void WritePresets(IReadOnlyList<PresetData> list)
    {
        int.TryParse(_ini.Get(PresetSection, "Count", "0"), out var old);
        for (int i = 0; i < list.Count; i++)
        {
            _ini.Set(PresetSection, $"Name{i}", list[i].Name);
            _ini.Set(PresetSection, $"Json{i}", PresetJson.ToLine(list[i]));
        }
        for (int i = list.Count; i < old; i++)
        {
            _ini.Remove(PresetSection, $"Name{i}");
            _ini.Remove(PresetSection, $"Json{i}");
        }
        _ini.Set(PresetSection, "Count", list.Count.ToString());
        Save();
    }

    /// <summary>同じ名前があれば上書き、なければ追加。</summary>
    public void SavePreset(PresetData p)
    {
        var list = Presets.ToList();
        var i = list.FindIndex(x => x.Name == p.Name);
        if (i >= 0) list[i] = p; else list.Add(p);
        WritePresets(list);
    }

    public void DeletePreset(string name) => WritePresets(Presets.Where(p => p.Name != name).ToList());

    /// <summary>いまの設定をプリセットにまとめる。</summary>
    public PresetData CapturePreset(string name) => new()
    {
        Name = name,
        Format = Format,
        Bitrate = BitrateKbps,
        SampleRate = SampleRate,
        Channels = Channels,
        Compression = Compression,
        Inputs = Inputs.Select(i => new PresetInput { Key = i.Key, GainDb = i.GainDb, Enabled = i.Enabled }).ToList(),
        SeparateFiles = SeparateFiles,
        SplitSeconds = SplitSeconds,
        Template = Template,
        Pattern = Pattern,
        DateFolder = DateFolder,
        DateFolderFormat = DateFolderFormat,
        Tags = Tags,
    };

    /// <summary>プリセットの内容を設定へ書き込む（保存先フォルダと番号は変えない）。値は読み出し側で範囲に丸められる。</summary>
    public void ApplyPreset(PresetData p)
    {
        Format = OutputFormat.Get(p.Format).Key;
        BitrateKbps = EncodeOptions.Bitrates.Contains(p.Bitrate) ? p.Bitrate : 192;
        SampleRate = p.SampleRate;
        Channels = p.Channels;
        SetCompressionFor(Format, p.Compression);
        Inputs = p.Inputs.Take(MaxInputs)
            .Select(i => new InputSetting(i.Key, Math.Clamp(i.GainDb, MinGainDb, MaxGainDb), i.Enabled)).ToList();
        SeparateFiles = p.SeparateFiles;
        SplitSeconds = p.SplitSeconds;
        Template = p.Template;
        Pattern = string.IsNullOrWhiteSpace(p.Pattern) ? FileNamer.DefaultPattern : p.Pattern;
        DateFolder = p.DateFolder;
        DateFolderFormat = string.IsNullOrWhiteSpace(p.DateFolderFormat) ? FileNamer.DefaultDateFormat : p.DateFolderFormat;
        Tags = p.Tags ?? TagInfo.Empty;
        CurrentPreset = p.Name;
    }
}
