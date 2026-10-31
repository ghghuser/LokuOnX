using System.Reflection;

namespace Recorder.Core;

/// <summary>アプリ名などの定数。表示名はここ1か所だけで管理する（PROJECT.md §10）。</summary>
public static class AppInfo
{
    public const string Name = "LokuOnX";

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
