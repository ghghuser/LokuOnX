using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>オプション欄のホットキー（PROJECT.md §3-8）。欄をクリックしてキーを押すと割り当てる。値は Settings へ即保存。</summary>
public partial class HotkeyPanel : System.Windows.Controls.UserControl
{
    private Settings _s = null!;
    private bool _loading;

    /// <summary>ホットキーの設定が変わった（登録し直すため）。</summary>
    public event Action? Changed;

    /// <summary>キーの割り当て中（欄にフォーカスがある間）。登録済みのホットキーを一時的に外さないと、押したキーを受け取れない。</summary>
    public event Action<bool>? CaptureChanged;

    public HotkeyPanel() => InitializeComponent();

    private void Box_Focus(object sender, KeyboardFocusChangedEventArgs e) =>
        CaptureChanged?.Invoke(StartBox.IsKeyboardFocused || PauseBox.IsKeyboardFocused || SplitBox.IsKeyboardFocused);

    public void Init(Settings settings)
    {
        _s = settings;
        Reload();
    }

    public void Reload()
    {
        _loading = true;
        OnCheck.IsChecked = _s.HotkeysOn;
        StartBox.Text = HotkeySpec.Format(_s.HotkeyStart);
        PauseBox.Text = HotkeySpec.Format(_s.HotkeyPause);
        SplitBox.Text = HotkeySpec.Format(_s.HotkeySplit);
        _loading = false;
    }

    private void On_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.HotkeysOn = OnCheck.IsChecked == true;
        Changed?.Invoke();
    }

    /// <summary>登録の結果などを、この欄の下に出す（エラーは赤文字）。空なら隠す。</summary>
    public void SetMessage(string text, bool error)
    {
        MessageText.Text = text;
        MessageText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Foreground = error ? new SolidColorBrush(Color.FromRgb(0xD3, 0x33, 0x33)) : Brushes.Gray;
        MessageText.FontWeight = error ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private TextBox BoxOf(string? tag) => tag switch
    {
        "start" => StartBox,
        "pause" => PauseBox,
        _ => SplitBox,
    };

    private void Save(string? tag, string value)
    {
        switch (tag)
        {
            case "start": _s.HotkeyStart = value; break;
            case "pause": _s.HotkeyPause = value; break;
            default: _s.HotkeySplit = value; break;
        }
        BoxOf(tag).Text = value;
        Changed?.Invoke();
    }

    /// <summary>既定のキーに戻す。ほかの操作がそのキーを使っていたら、割り当てない。</summary>
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var tag = b.Tag as string;
        var text = HotkeySpec.Format(tag switch
        {
            "start" => Settings.DefaultHotkeyStart,
            "pause" => Settings.DefaultHotkeyPause,
            _ => Settings.DefaultHotkeySplit,
        });
        foreach (var other in new[] { "start", "pause", "split" })
        {
            if (other != tag && BoxOf(other).Text == text)
            {
                SetMessage("既定のキー " + text + " は、ほかの操作に割り当て済みのため戻せません。先にそちらを変えるか解除してください。", true);
                return;
            }
        }
        SetMessage("", false);
        Save(tag, text);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        SetMessage("", false);
        Save(b.Tag as string, "");
    }

    private void Key_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab or Key.Escape or Key.ImeProcessed) return;
        if (mods == ModifierKeys.None && key is Key.Delete or Key.Back)
        {
            SetMessage("", false);
            Save(box.Tag as string, "");
            return;
        }
        var name = KeyName(key);
        if (name == null || mods == ModifierKeys.None)
        {
            SetMessage("Ctrl・Alt・Shift・Win のどれかを押しながら、F1〜F24・英字・数字などのキーを押してください。", true);
            return;
        }
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        var text = HotkeySpec.Format(string.Join("+", parts));
        if (text.Length == 0)
        {
            SetMessage("そのキーは使えません。", true);
            return;
        }
        var tag = box.Tag as string;
        foreach (var other in new[] { "start", "pause", "split" })
        {
            if (other != tag && BoxOf(other).Text == text)
            {
                SetMessage("同じキーを2つの操作に割り当てることはできません: " + text, true);
                return;
            }
        }
        SetMessage("", false);
        Save(tag, text);
    }

    private static string? KeyName(Key k)
    {
        if (k >= Key.F1 && k <= Key.F24) return "F" + (k - Key.F1 + 1);
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
        return k switch
        {
            Key.Space => "Space",
            Key.Pause => "Pause",
            Key.PageUp => "PageUp",
            Key.Next => "PageDown",
            Key.End => "End",
            Key.Home => "Home",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            _ => null,
        };
    }
}
