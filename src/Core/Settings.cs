namespace Recorder.Core;

/// <summary>
/// 設定（settings.ini）。値を変えた時点で即保存する。
/// ReadOnly のとき（同じフォルダの2つ目の起動）は iniに書き込まず、変更はその起動中だけ有効（PROJECT.md §3-6）。
/// </summary>
public sealed partial class Settings
{
    private const string Section = "Recorder";
    private readonly IniFile _ini;

    public bool ReadOnly { get; }

    /// <summary>プリセットのJSONを入れるフォルダ（exeと同じ場所の Presets。PROJECT.md §3-6）。</summary>
    private readonly string _presetDir;

    /// <summary>直近の保存エラー（なければ null）。</summary>
    public string? SaveError { get; private set; }

    public Settings(string path, bool readOnly)
    {
        _ini = new IniFile(path);
        ReadOnly = readOnly;
        _presetDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", "Presets");
        if (!_ini.Existed && !readOnly)
        {
            _ini.Set(Section, "Format", "flac");
            _ini.Set(Section, "NextNo", "1");
            Save();
        }
        MigratePresets();   // M5 の初版は ini に入れていた。Presets フォルダのファイルへ移す
        EnsureTemplates();   // 旧版の ini にはテンプレートが無いので、足りなければ初期テンプレートを登録
    }

    private string Get(string key, string fallback) => _ini.Get(Section, key, fallback);

    private void Set(string key, string value)
    {
        _ini.Set(Section, key, value);
        Save();
    }

    private void Save()
    {
        if (ReadOnly) return;
        try { _ini.Save(); SaveError = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = ex.Message;
        }
    }

    /// <summary>最後に選んだデバイスの Key。</summary>
    public string Device { get => Get("Device", ""); set => Set("Device", value); }

    /// <summary>"flac" / "mp3" / "m4a" / "aac" / "wav" / "ogg" / "opus"（OutputFormat.Key）。既定 flac。</summary>
    public string Format
    {
        get => OutputFormat.Get(Get("Format", "flac")).Key;
        set => Set("Format", value);
    }

    /// <summary>mp3 / aac / ogg / opus のビットレート(kbps)。既定192。flac・wav では使わない。</summary>
    public int BitrateKbps
    {
        get => int.TryParse(Get("Bitrate", "192"), out var n) && EncodeOptions.Bitrates.Contains(n) ? n : 192;
        set => Set("Bitrate", value.ToString());
    }

    /// <summary>サンプリング周波数。0 = デバイスに合わせる（自動）。</summary>
    public int SampleRate
    {
        get => int.TryParse(Get("SampleRate", "0"), out var n) && (n == 0 || EncodeOptions.SampleRates.Contains(n)) ? n : 0;
        set => Set("SampleRate", value.ToString());
    }

    /// <summary>チャンネル数。0 = デバイスに合わせる（自動）、1 = モノ、2 = ステレオ。</summary>
    public int Channels
    {
        get => int.TryParse(Get("Channels", "0"), out var n) && (n is 0 or 1 or 2) ? n : 0;
        set => Set("Channels", value.ToString());
    }

    /// <summary>形式・音質・圧縮率・タグをまとめたもの。</summary>
    public EncodeOptions Encode => new(Format, BitrateKbps, SampleRate, Channels, Compression, Tags);

    public string Folder
    {
        get => Get("Folder", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.Name, "Rec"));
        set => Set("Folder", value);
    }

    /// <summary>次の録音に使う {No}。終了後も維持する。</summary>
    public int NextNo
    {
        get => int.TryParse(Get("NextNo", "1"), out var n) && n >= 1 ? n : 1;
        set => Set("NextNo", value.ToString());
    }
}
