using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("予期しないエラーが発生しました。\n" + args.Exception.Message,
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // メイン画面の入力欄は、Enter で確定する（欄から離れたときと同じ処理が走る）
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnTextBoxKeyDown));

        // 同じフォルダのexeだけを多重起動とみなす（別フォルダのコピーは警告なしで起動）
        var first = InstanceGuard.TryAcquire(AppPaths.Dir);
        if (!first)
        {
            var answer = MessageBox.Show(
                $"すでに『{AppInfo.Name}』が起動しています。設定は保存されませんが、それでも起動しますか？",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
        }

        var settings = new Settings(AppPaths.SettingsIni, readOnly: !first);
        UI.ThemeManager.Apply(settings.Theme);   // 画面の色（自動／ライト／ダーク）。ウィンドウを作る前に
        new UI.MainWindow(settings).Show();
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
