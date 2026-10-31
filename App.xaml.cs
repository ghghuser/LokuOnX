using System.Windows;

namespace Recorder;

public partial class App : Application
{
    private Mutex? _mutex;

    /// <summary>最初に起動した方ならtrue。falseの間は設定ini・{No}カウンタを書き込まない（M5以降で使う）。</summary>
    public static bool IsPrimaryInstance { get; private set; } = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\" + AppInfo.Name + "_SingleInstance", out var createdNew);
        IsPrimaryInstance = createdNew;

        if (!createdNew)
        {
            var message = $"すでに『{AppInfo.Name}』が起動しています。設定は保存されませんが、それでも起動しますか？";
            var result = MessageBox.Show(message, AppInfo.Name, MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
        new UI.MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (IsPrimaryInstance)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
