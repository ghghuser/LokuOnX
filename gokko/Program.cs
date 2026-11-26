using System.Reflection;
using System.Windows;
using Recorder.Core;

namespace Gokko;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        using var mutex = new Mutex(true, "Gokko4DJ." + ControlProtocol.PipeName(AppContext.BaseDirectory, "Gokko4DJ"), out var first);
        if (!first) return 0;   // 同じフォルダのexeの二重起動はしない
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lang.ja.ini"))
            Loc.Init(res, AppContext.BaseDirectory, "gokko-ja");   // 外部ファイルは読まない（LokuOnX の Lang/ja.ini と混ざらないよう、埋め込みだけを使う）
        UI.ThemeManager.LoadStyles();
        UI.ThemeManager.Apply(ReadThemeMode());
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            MessageBox.Show(Loc.Get("g.app.err", e.Exception.Message), Loc.Get("g.title"), MessageBoxButton.OK, MessageBoxImage.Error);
        };
        return app.Run(new UI.MainWindow());
    }

    /// <summary>設定ファイルの画面の色（auto／light／dark）だけ先に読む（ウィンドウを作る前に色を決めるため）。</summary>
    private static string ReadThemeMode()
    {
        try { return new IniFile(System.IO.Path.Combine(AppContext.BaseDirectory, "gokko.ini")).Get("Gokko", "Theme", "auto"); }
        catch (Exception) { return "auto"; }
    }
}
