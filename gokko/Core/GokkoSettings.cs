using System.Globalization;
using Recorder.Core;

namespace Gokko.Core;

/// <summary>Gokko4DJ の設定（exe の隣の gokko.ini）。出力デバイス・全体の音量・ページ・登録アプリ・パッド。</summary>
public sealed class GokkoSettings
{
    public const double MinMasterDb = PadConfig.MinDb;
    public const double MaxMasterDb = 6;

    private readonly IniFile _ini;

    public PadBank Pads { get; } = new();

    /// <summary>出力デバイスの ID。空なら Windows の既定。</summary>
    public string OutputDeviceId { get; set; } = "";

    public double MasterDb { get; set; }

    public int Page { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public List<string> Launchers { get; private set; } = new();

    public GokkoSettings(string path)
    {
        _ini = new IniFile(path);
        OutputDeviceId = _ini.Get("App", "OutputDevice", "");
        if (double.TryParse(_ini.Get("App", "MasterDb", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var db) && double.IsFinite(db))
            MasterDb = Math.Clamp(db, MinMasterDb, MaxMasterDb);
        if (int.TryParse(_ini.Get("App", "Page", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
            Page = Math.Clamp(page, 0, PadBank.Pages - 1);
        MinimizeToTray = _ini.Get("App", "MinimizeToTray", "1") != "0";
        Launchers = LauncherList.Clean(Enumerable.Range(1, 5).Select(i => (string?)_ini.Get("Launcher", "Launcher" + i, "")));
        Pads.Load(_ini);
    }

    /// <summary>保存。失敗したら例外（呼び出し側で扱う）。</summary>
    public void Save()
    {
        _ini.Set("App", "OutputDevice", OutputDeviceId);
        _ini.Set("App", "MasterDb", MasterDb.ToString("0.0", CultureInfo.InvariantCulture));
        _ini.Set("App", "Page", Page.ToString(CultureInfo.InvariantCulture));
        _ini.Set("App", "MinimizeToTray", MinimizeToTray ? "1" : "0");
        for (var i = 1; i <= 5; i++) _ini.Remove("Launcher", "Launcher" + i);
        for (var i = 0; i < Launchers.Count && i < 5; i++) _ini.Set("Launcher", "Launcher" + (i + 1), Launchers[i]);
        Pads.Save(_ini);
        _ini.Save();
    }

    public void SetLaunchers(IEnumerable<string?> paths) => Launchers = LauncherList.Clean(paths);
}
