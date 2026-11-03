using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>ファイル名の変更。名前（拡張子なし）を入力して、変えたあとのパスを Target に返す。エラーはダイアログ内に赤文字で出す。</summary>
public partial class RenameDialog : Window
{
    private readonly string _path;

    /// <summary>OK のとき、変更後のフルパス。</summary>
    public string? Target { get; private set; }

    public RenameDialog(string theme, string path)
    {
        InitializeComponent();
        _path = path;
        ThemeManager.Attach(this, theme);
        NameBox.Text = FileOps.Stem(path);
        ExtText.Text = Path.GetExtension(path);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var target = FileOps.PlanRename(_path, NameBox.Text, out var error);
        if (target == null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Target = target;
        DialogResult = true;
    }
}
