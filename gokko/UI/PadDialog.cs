using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gokko.Core;
using Microsoft.Win32;
using Recorder.Core;

namespace Gokko.UI;

/// <summary>パッド1つの設定ウィンドウ（音のファイル・名前・鳴らし方・音量・色）。</summary>
public sealed class PadDialog : Window
{
    private readonly TextBox _file = new() { MinWidth = 280 };
    private readonly TextBox _name = new() { MaxLength = PadConfig.MaxNameLength };
    private readonly ComboBox _mode = new();
    private readonly Slider _vol = new() { Minimum = PadConfig.MinDb, Maximum = PadConfig.MaxDb, TickFrequency = 0.5, IsSnapToTickEnabled = true, MinWidth = 200 };
    private readonly TextBlock _volText = new() { Width = 64, TextAlignment = TextAlignment.Right };
    private readonly List<Border> _swatches = new();
    private int _color;

    public PadConfig? Result { get; private set; }

    public PadDialog(PadConfig initial)
    {
        Title = Loc.Get("g.dlg.title");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ThemeManager.Attach(this);

        _file.Text = initial.FilePath;
        _name.Text = initial.Name;
        foreach (var m in new[] { "g.mode.once", "g.mode.loop", "g.mode.hold" }) _mode.Items.Add(Loc.Get(m));
        _mode.SelectedIndex = (int)initial.Mode;
        _vol.Value = initial.VolumeDb;
        _vol.ValueChanged += (_, _) => ShowVolume();
        ShowVolume();
        _color = initial.Color;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Row(int row, string label, UIElement content)
        {
            var t = new TextBlock { Text = Loc.Get(label), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 6) };
            Grid.SetRow(t, row);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 0, 6);
            grid.Children.Add(t);
            grid.Children.Add(content);
        }

        var browse = new Button { Content = Loc.Get("g.dlg.browse"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var filePanel = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        filePanel.Children.Add(browse);
        filePanel.Children.Add(_file);
        Row(0, "g.dlg.file", filePanel);
        Row(1, "g.dlg.name", _name);
        Row(2, "g.dlg.mode", _mode);
        var volPanel = new DockPanel();
        DockPanel.SetDock(_volText, Dock.Right);
        volPanel.Children.Add(_volText);
        volPanel.Children.Add(_vol);
        Row(3, "g.dlg.volume", volPanel);

        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < PadConfig.ColorCount; i++)
        {
            var idx = i;
            var sw = new Border { Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(4), Background = PadBrushes.Fill(i), BorderThickness = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand };
            sw.MouseLeftButtonUp += (_, _) => { _color = idx; ShowColor(); };
            _swatches.Add(sw);
            colors.Children.Add(sw);
        }
        Row(4, "g.dlg.color", colors);
        ShowColor();

        var ok = new Button { Content = Loc.Get("g.dlg.ok"), IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.Get("g.dlg.cancel"), IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 5);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void ShowVolume() => _volText.Text = _vol.Value <= PadConfig.MinDb ? Loc.Get("g.master.mute") : $"{_vol.Value:0.0} dB";

    private void ShowColor()
    {
        for (var i = 0; i < _swatches.Count; i++)
            _swatches[i].BorderBrush = i == _color ? Brushes.White : Brushes.Transparent;
    }

    private void Browse()
    {
        var dlg = new OpenFileDialog { Title = Loc.Get("g.dlg.pick"), Filter = Loc.Get("g.dlg.filter"), InitialDirectory = SeFolder.Initial(_file.Text.Trim().Trim('"')) };
        if (dlg.ShowDialog(this) == true) _file.Text = dlg.FileName;
    }

    private void Accept()
    {
        if (_file.Text.Trim().Trim('"').Length == 0)
        {
            MessageBox.Show(this, Loc.Get("g.dlg.err.nofile"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = new PadConfig(_name.Text, _file.Text, (PadMode)Math.Max(0, _mode.SelectedIndex), _vol.Value, _color).Normalize();
        DialogResult = true;
    }
}
