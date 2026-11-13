using System.Text;

namespace Recorder.Core;

/// <summary>
/// Settings のうち、プロファイルに関する部分（PROJECT.md §3-6）。
/// プロファイルは exe と同じ場所の Profiles フォルダに「名前.json」で1件ずつ保存する（そのままバックアップ・共有できる）。
/// </summary>
public sealed partial class Settings
{
    /// <summary>保存されているプロファイル（名前順）。読めないファイルは飛ばす。</summary>
    public IReadOnlyList<ProfileData> Profiles
    {
        get
        {
            var list = new List<ProfileData>();
            try
            {
                if (!Directory.Exists(_profileDir)) return list;
                foreach (var file in Directory.EnumerateFiles(_profileDir, "*.json"))
                {
                    try
                    {
                        var parsed = ProfileJson.Parse(File.ReadAllText(file, Encoding.UTF8), out _);
                        if (parsed is { Count: > 0 }) list.Add(parsed[0]);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>プロファイルのJSONを入れるフォルダ（画面の「Profilesフォルダを開く」で開く）。</summary>
    public string ProfileFolder => _profileDir;

    private string ProfileFile(string name) => Path.Combine(_profileDir, FileNamer.Sanitize(name) + ".json");

    /// <summary>同じ名前があれば上書き、なければ追加。2つ目の起動（ReadOnly）では書かない。</summary>
    public void SaveProfile(ProfileData p)
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(_profileDir);
            var path = ProfileFile(p.Name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, ProfileJson.ToFile(p), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            SaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = ex.Message;
        }
    }

    public void DeleteProfile(string name)
    {
        if (ReadOnly) return;
        try { File.Delete(ProfileFile(name)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SaveError = ex.Message; }
    }

    /// <summary>M5初版で ini の [Profiles] に入れていたものを、Profiles フォルダのファイルへ移す。</summary>
    private void MigrateProfiles()
    {
        if (ReadOnly || _ini.Get("Presets", "Count", "") == "") return;
        int.TryParse(_ini.Get("Presets", "Count", "0"), out var count);
        for (int i = 0; i < count; i++)
        {
            var parsed = ProfileJson.Parse(_ini.Get("Presets", $"Json{i}", ""), out _);
            if (parsed is { Count: > 0 } && !File.Exists(ProfileFile(parsed[0].Name))) SaveProfile(parsed[0]);
            _ini.Remove("Presets", $"Name{i}");
            _ini.Remove("Presets", $"Json{i}");
        }
        _ini.Remove("Presets", "Count");
        Save();
    }

    /// <summary>いまの設定をプロファイルにまとめる。</summary>
    public ProfileData CaptureProfile(string name) => new()
    {
        Name = name,
        Format = Format,
        Bitrate = BitrateKbps,
        SampleRate = SampleRate,
        Channels = Channels,
        Compression = Compression,
        Inputs = Inputs.Select(i => new ProfileInput { Key = i.Key, GainDb = i.GainDb, Enabled = i.Enabled }).ToList(),
        SeparateFiles = SeparateFiles,
        SplitSeconds = SplitSeconds,
        Template = Template,
        Pattern = Pattern,
        DateFolder = DateFolder,
        DateFolderFormat = DateFolderFormat,
        Tags = Tags,
        Outputs = Outputs.Select(o => new ProfileOutput
        {
            Name = o.Name,
            DeviceId = o.DeviceId,
            VolumeDb = o.VolumeDb,
            Sends = Enumerable.Range(0, OutputConfig.InputSlots).Select(i => new ProfileSend { On = o.SendAt(i).On, Db = o.SendAt(i).Db }).ToList(),
            Fx = o.Fx is { IsDefault: false } ? o.Fx.Clone() : null,
        }).ToList(),
        InputNames = InputNames.ToList(),
        InputEffects = InputEffects.Select(e => e.Clone()).ToList(),
    };

    /// <summary>プロファイルの内容を設定へ書き込む（保存先フォルダと番号は変えない）。値は読み出し側で範囲に丸められる。</summary>
    public void ApplyProfile(ProfileData p)
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
        if (p.InputNames != null) InputNames = p.InputNames;
        if (p.InputEffects != null) InputEffects = p.InputEffects.Select(e => e ?? new EffectSetting()).ToList();
        if (p.Outputs is { Count: > 0 })
            Outputs = p.Outputs.Take(OutputConfig.MaxOutputs).Select((o, i) => new OutputConfig(o.Name, o.DeviceId, o.VolumeDb,
                Enumerable.Range(0, OutputConfig.InputSlots).Select(k => k < (o.Sends?.Count ?? 0) ? new OutSend(o.Sends![k].On, o.Sends[k].Db) : new OutSend()).ToList(),
                o.Fx).Normalize(i)).ToList();
        CurrentProfile = p.Name;
    }
}
