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
        int.TryParse(_ini.Get(Section, "Page", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pg);
        Page = Math.Clamp(pg, 0, PadBank.Pages - 1);
        var apps = new List<string>();
        for (var i = 1; i <= LauncherList.Max; i++) apps.Add(_ini.Get("Launcher", "App" + i, ""));
        Launcher = LauncherList.Clean(apps);
        Bank.Load(_ini);
    }

    public IniFile Ini => _ini;
    public PadBank Bank { get; } = new();

    /// <summary>出力デバイスの ID。空＝Windows の既定の出力。</summary>
    public string OutputDeviceId { get; set; }
    public double MasterDb { get; set; }
    public int Page { get; set; }
    public List<string> Launcher { get; set; }

    public void Save()
    {
        _ini.Set(Section, "OutputDevice", OutputDeviceId);
        _ini.Set(Section, "MasterDb", MasterDb.ToString("0.0", CultureInfo.InvariantCulture));
        _ini.Set(Section, "Page", Page.ToString(CultureInfo.InvariantCulture));
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
