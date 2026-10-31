using System.Windows;
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
        new UI.MainWindow(settings).Show();
    }
}
