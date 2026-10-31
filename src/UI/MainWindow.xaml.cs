using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Name;
        TitleText.Text = AppInfo.Name;
        VersionText.Text = $"バージョン {AppInfo.Version}";
    }
}
