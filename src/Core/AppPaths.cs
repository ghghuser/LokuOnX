namespace Recorder.Core;

/// <summary>exe と同じフォルダを基準にした場所（ポータブル運用）。</summary>
public static class AppPaths
{
    public static string Dir { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static string SettingsIni => Path.Combine(Dir, "settings.ini");

    public static string Ffmpeg => Path.Combine(Dir, "ffmpeg", "ffmpeg.exe");
}
