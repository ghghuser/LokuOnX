using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder;

public partial class App : Application
{
    private bool _errorDialogOpen;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 画面の文言（exe 内の既定 → exe の隣の Lang/ja.ini で上書き）。画面を作る前に読む
        using (var res = typeof(App).Assembly.GetManifestResourceStream("Lang.ja.ini")) Loc.Init(res, AppPaths.Dir, "ja");
        // 想定外のエラーは lokuonx-crash.log に記録する。画面の処理の例外は握りつぶして、録音（別スレッド）は続ける
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("UI: " + args.Exception);
            args.Handled = true;
            if (_errorDialogOpen) return;   // タイマーなどで同じ例外が続いても、ダイアログを重ねない
            _errorDialogOpen = true;
            try
            {
                MessageBox.Show(Loc.Get("app.s01", args.Exception.Message, CrashLog.FileName),
                    AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _errorDialogOpen = false; }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write("AppDomain: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => { CrashLog.Write("Task: " + args.Exception); args.SetObserved(); };

        // メイン画面の入力欄は、Enter で確定する（欄から離れたときと同じ処理が走る）
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnTextBoxKeyDown));

        // 同じフォルダのexeだけを多重起動とみなす（別フォルダのコピーは警告なしで起動）
        var first = InstanceGuard.TryAcquire(AppPaths.Dir);
        if (!first)
        {
            var answer = MessageBox.Show(
                Loc.Get("app.s02", AppInfo.Name),
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
        }

        var settings = new Settings(AppPaths.SettingsIni, readOnly: !first);
        UI.ThemeManager.Apply(settings.Theme);   // 画面の色（自動／ライト／ダーク）。ウィンドウを作る前に
        try
        {
            new UI.MainWindow(settings).Show();
        }
        catch (Exception ex)
        {
            // 画面が作れなかったときは、窓の無いプロセスを残さず終了する（多重起動の誤検知を防ぐ）
            CrashLog.Write("Startup: " + ex);
            MessageBox.Show(Loc.Get("app.s03", ex.Message, CrashLog.FileName),
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb || tb.IsReadOnly || tb.AcceptsReturn) return;
        if (Window.GetWindow(tb) is not UI.MainWindow window) return;
        // 次の欄へフォーカスを移す（Tab と同じ）。欄から離れたときの LostFocus の処理（設定への反映）が走る
        tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }
}
