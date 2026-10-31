using System.IO;
using System.Reflection;
using System.Windows;

namespace Recorder.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        VersionText.Text = "バージョン " + version;

        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        FfmpegText.Text = File.Exists(ffmpegPath)
            ? "FFmpeg: 同梱を確認しました"
            : "FFmpeg: 見つかりません";
    }
}
