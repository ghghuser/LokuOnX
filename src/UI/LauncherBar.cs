using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// メイン画面上部のアプリランチャー（M17）。登録したアプリ（最大5個）をアイコンのボタンで並べ、押すと起動する。
/// 右クリック＝変更・削除。いちばん右の「＋」でファイルを選んで登録（ファイル選択のみ）。登録は設定（Settings.LauncherPaths）へすぐ保存する。
/// 起動に失敗したときは、ポップアップではなく <see cref="Notice"/> でログ欄へ知らせる。
/// </summary>
public sealed class LauncherBar : StackPanel
{
    private const double ButtonSize = 28;

    private Settings? _s;
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ログ欄へ出す知らせ（起動の失敗など）。</summary>
    public Action<string>? Notice { get; set; }

    public LauncherBar()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void Init(Settings settings)
    {
        _s = settings;
        Rebuild();
    }

    /// <summary>登録を読み直して並べ直す（プロファイルなどで設定が変わったとき）。</summary>
    public void Rebuild()
    {
        Children.Clear();
        if (_s == null) return;
        var paths = _s.LauncherPaths;
        for (int i = 0; i < paths.Count; i++) Children.Add(MakeApp(i, paths[i]));
        if (paths.Count < LauncherList.Max) Children.Add(MakeAdd());
    }

    private Button MakeBase(object content, string tip)
    {
        var b = new Button
        {
            Content = content,
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return b;
    }

    private Button MakeAdd()
    {
        var b = MakeBase(new TextBlock { Text = "＋", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Loc.Get("launcher.add.tip"));
        b.Click += (_, _) => Pick(-1);
        return b;
    }

    private Button MakeApp(int index, string path)
    {
        var image = new System.Windows.Controls.Image { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = MakeBase(image, Loc.Get("launcher.app.tip", LauncherList.Title(path), path));
        SetIcon(image, path);
        b.Click += (_, _) => Launch(path);
        var menu = new ContextMenu();
        var change = new MenuItem { Header = Loc.Get("launcher.menu.change") };
        change.Click += (_, _) => Pick(index);
        var remove = new MenuItem { Header = Loc.Get("launcher.menu.remove") };
        remove.Click += (_, _) => Remove(index);
        menu.Items.Add(change);
        menu.Items.Add(remove);
        b.ContextMenu = menu;
        return b;
    }

    /// <summary>アイコンはアプリ本体から取り出す（遠いドライブで待たされないよう、別スレッドで）。取れなければ「▶」。</summary>
    private async void SetIcon(System.Windows.Controls.Image image, string path)
    {
        if (_icons.TryGetValue(path, out var cached) && cached != null) { image.Source = cached; return; }
        ImageSource? src = null;
        try
        {
            src = await Task.Run(() =>
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var bmp = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return (ImageSource)bmp;
            });
        }
        catch (Exception) { }
        if (src != null)
        {
            _icons[path] = src;
            image.Source = src;
        }
        else if (image.Parent is Button b)
        {
            b.Content = new TextBlock { Text = "▶", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
    }

    private void Launch(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) info.WorkingDirectory = dir;
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex)
        {
            Notice?.Invoke(Loc.Get("launcher.err.start", LauncherList.Title(path), ex.Message));
        }
    }

    /// <summary>ファイルを選んで登録する。index＝-1 は追加、0以上はその位置のアプリを置き換える。</summary>
    private void Pick(int index)
    {
        if (_s == null) return;
        var dlg = new OpenFileDialog
        {
            Title = Loc.Get("launcher.pick.title"),
            Filter = Loc.Get("launcher.pick.filter"),
            DereferenceLinks = false,   // ショートカット（.lnk）はそのまま登録する
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        var list = _s.LauncherPaths.ToList();
        if (index >= 0 && index < list.Count) list[index] = dlg.FileName;
        else list.Add(dlg.FileName);
        _s.LauncherPaths = list;
        Rebuild();
    }

    private void Remove(int index)
    {
        if (_s == null) return;
        var list = _s.LauncherPaths.ToList();
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        _s.LauncherPaths = list;
        Rebuild();
    }
}
