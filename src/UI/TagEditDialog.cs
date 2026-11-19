using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>保存済みのファイルのタグを編集するダイアログ（M15）。「保存」を押すと Result に入力内容を返す（書き込みと確認は呼び出し側）。</summary>
public sealed class TagEditDialog : Window
{
    private readonly TextBox[] _boxes = new TextBox[6];

    /// <summary>「保存」のとき、入力されたタグ。</summary>
    public TagInfo? Result { get; private set; }

    public TagEditDialog(string theme, string path, TagInfo current)
    {
        Title = Loc.Get("tagedit.title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Meiryo UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "WindowFg");

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labels = new[] { "tagpanel.textblock1.text", "tagpanel.textblock2.text", "tagpanel.textblock3.text", "tagpanel.textblock4.text", "tagpanel.textblock5.text", "tagpanel.textblock6.text" };
        var values = new[] { current.Title, current.Artist, current.Album, current.Genre, current.Year, current.Comment };
        for (int i = 0; i < 6; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = Loc.Get(labels[i]), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 6) };
            var box = new TextBox { Text = values[i], VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 3, 2, 3), Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(label, i);
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 1);
            grid.Children.Add(label);
            grid.Children.Add(box);
            _boxes[i] = box;
        }
        root.Children.Add(grid);
        root.Children.Add(new TextBlock { Text = Loc.Get("tagedit.note"), Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14) });

        var ok = new Button { Content = Loc.Get("tagedit.save"), Width = 90, Padding = new Thickness(0, 5, 0, 5), IsDefault = true };
        var cancel = new Button { Content = Loc.Get("tagedit.cancel"), Width = 90, Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) =>
        {
            Result = new TagInfo(_boxes[0].Text.Trim(), _boxes[1].Text.Trim(), _boxes[2].Text.Trim(), _boxes[3].Text.Trim(), _boxes[4].Text.Trim(), _boxes[5].Text.Trim());
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        ThemeManager.Attach(this, theme);
        Loaded += (_, _) => { _boxes[0].Focus(); _boxes[0].SelectAll(); };
    }
}
