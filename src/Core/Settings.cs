namespace Recorder.Core;

/// <summary>
/// 設定（settings.ini）。値を変えた時点で即保存する。
/// ReadOnly のとき（同じフォルダの2つ目の起動）は iniに書き込まず、変更はその起動中だけ有効（PROJECT.md §3-6）。
/// </summary>
public sealed class Settings
{
    private const string Section = "Recorder";
    private readonly IniFile _ini;

    public bool ReadOnly { get; }

    /// <summary>直近の保存エラー（なければ null）。</summary>
    public string? SaveError { get; private set; }

    public Settings(string path, bool readOnly)
    {
        _ini = new IniFile(path);
        ReadOnly = readOnly;
        if (!_ini.Existed && !readOnly)
        {
            _ini.Set(Section, "Format", "flac");
            _ini.Set(Section, "NextNo", "1");
            Save();
        }
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

    /// <summary>"flac" または "mp3"。既定 flac。</summary>
    public string Format
    {
        get => Get("Format", "flac") is "mp3" ? "mp3" : "flac";
        set => Set("Format", value);
    }

    public string Folder
    {
        get => Get("Folder", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.Name));
        set => Set("Folder", value);
    }

    /// <summary>次の録音に使う {No}。終了後も維持する。</summary>
    public int NextNo
    {
        get => int.TryParse(Get("NextNo", "1"), out var n) && n >= 1 ? n : 1;
        set => Set("NextNo", value.ToString());
    }
}
