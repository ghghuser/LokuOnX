namespace Recorder
{
    /// <summary>
    /// アプリ名の唯一の定義場所。画面タイトル・警告ポップアップ・Mutex名などはここを参照する。
    /// exe名・ZIP名は Recorder.csproj の AssemblyName と build.yml 側で LokuonX にそろえる。
    /// </summary>
    public static class AppInfo
    {
        public const string Name = "LokuonX";
    }
}
