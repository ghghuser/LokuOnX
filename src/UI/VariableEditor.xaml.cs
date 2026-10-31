using System.Windows;
using System.Windows.Controls;

namespace Recorder.UI;

/// <summary>
/// 変数を使う入力欄（ファイル名パターン・日付フォルダ名）の編集ウィンドウ。
/// 変数の説明と、押すとカーソル位置に挿入されるボタンを出す。OK で Result に結果が入る。
/// </summary>
public partial class VariableEditor : Window
{
    private readonly Func<string, string> _preview;

    /// <summary>OK を押したときの入力内容。</summary>
    public string Result { get; private set; }

    public VariableEditor(string title, string initial, IReadOnlyList<VarInfo> vars, Func<string, string> preview)
    {
        InitializeComponent();
        Title = title;
        Result = initial;
        _preview = preview;

        foreach (var v in vars)
        {
            var b = new Button
            {
                Content = v.Token,
                ToolTip = v.Description,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Focusable = false,   // 押してもキャレットが入力欄から動かないように
            };
            var token = v.Token;
            b.Click += (_, _) => Insert(token);
            Buttons.Children.Add(b);
        }
        HelpText.Text = string.Join("\n", vars.Select(v => $"{v.Token}　{v.Description}"))
                        + "\n\nコロンは自動で全角「：」になります。使えない文字は別の文字に置き換わります。";

        Box.Text = initial;
        Box.CaretIndex = Box.Text.Length;
        Loaded += (_, _) => Box.Focus();
        UpdatePreview();
    }

    private void Insert(string token)
    {
        var start = Box.SelectionStart;
        Box.SelectedText = token;
        Box.Select(start + token.Length, 0);
        Box.Focus();
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText == null) return;   // 初期化中
        PreviewText.Text = "例: " + _preview(Box.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = Box.Text;
        DialogResult = true;
    }
}
