using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>メイン画面のうち、タスクトレイ（アイコン・右クリックメニュー・最小化で格納）とグローバルホットキー（PROJECT.md §3-6, §3-8）。</summary>
public partial class MainWindow
{
    private const int CmdOpen = 1, CmdStart = 2, CmdPause = 3, CmdSplit = 4, CmdExit = 5;
    private const int HkStart = 1, HkPause = 2, HkSplit = 3;

    private MessageWindow? _msg;
    private TrayIcon? _tray;
    private HotkeyHost? _hotkeys;

    private void InitTray()
    {
        try
        {
            _msg = new MessageWindow();
            _tray = new TrayIcon(_msg) { MenuProvider = BuildTrayMenu };
            _tray.OpenRequested += ShowFromTray;
            _tray.CommandSelected += OnTrayCommand;
            _hotkeys = new HotkeyHost(_msg);
            _hotkeys.Pressed += OnHotkey;
        }
        catch (Exception ex)
        {
            _tray = null;
            _hotkeys = null;
            StatusText.Text = Loc.Get("tray.err.prepare", ex.Message);
        }
        ApplyTray();
        ApplyHotkeys();
    }

    /// <summary>歯車の設定ウィンドウから。タスクトレイの設定が変わった。</summary>
    private void OnTrayOptionsChanged() => ApplyTray();

    /// <summary>歯車の設定ウィンドウから。キーの割り当て中は、登録済みのキーを外す（押したキーを受け取るため）。終わったら登録し直す。</summary>
    private void OnHotkeyCapture(bool capturing)
    {
        if (capturing) _hotkeys?.UnregisterAll();
        else ApplyHotkeys();
    }

    /// <summary>オプション欄の変更を反映する（トレイの表示・録音ボタンの文字）。</summary>
    private void OnOptionsChanged()
    {
        ApplyTray();
        SetRecordButton(_session != null || _standby);
    }

    private void ApplyTray()
    {
        if (_tray != null) _tray.Visible = _settings.ShowTray;
    }

    private bool TrayUsable => _tray is { Visible: true } && _settings.ShowTray;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // 最小化したらトレイへ格納（アイコンが出ているときだけ。録音は続く）
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray && TrayUsable) Hide();
    }

    private void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        bool rec = _session != null;
        var start = Loc.Get(rec ? "tray.menu.stop" : _standby ? "tray.menu.standbyCancel" : _settings.AutoStart ? "tray.menu.standby" : "tray.menu.start");
        return new[]
        {
            new TrayMenuItem(CmdOpen, Loc.Get("tray.menu.show", AppInfo.Name)),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdStart, start, StartButton.IsEnabled),
            new TrayMenuItem(CmdPause, Loc.Get(_session?.Paused == true ? "tray.menu.resume" : "tray.menu.pause"), rec && PauseButton.IsEnabled),
            new TrayMenuItem(CmdSplit, Loc.Get("tray.menu.split"), rec && SplitButton.IsEnabled),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdExit, Loc.Get("tray.menu.exit")),
        };
    }

    private void OnTrayCommand(int id)
    {
        switch (id)
        {
            case CmdOpen: ShowFromTray(); break;
            case CmdStart: _ = ToggleAsync(); break;
            case CmdPause: TogglePause(); break;
            case CmdSplit: _ = SplitAsync(); break;
            case CmdExit:
                ShowFromTray();   // 録音中の確認ダイアログを見える位置に出すため、先に画面を出す
                Close();
                break;
        }
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HkStart: _ = ToggleAsync(); break;
            case HkPause: TogglePause(); break;
            case HkSplit: _ = SplitAsync(); break;
        }
    }

    /// <summary>
    /// ホットキーを登録し直す。登録できなかったキー（ほかのアプリが使っているなど）は、結果のメッセージ（赤文字用）で返す。
    /// 設定ウィンドウに出す。オプション欄の最下段には、ホットキーがONのとき割り当てを薄い文字で出す。
    /// </summary>
    private (string Text, bool Error) ApplyHotkeys()
    {
        if (_hotkeys == null) return ("", false);
        _hotkeys.UnregisterAll();
        if (!_settings.HotkeysOn) { ShowHotkeyInfo(false); return ("", false); }
        if (_settings.ReadOnly)
        {
            ShowHotkeyInfo(false);
            return (Loc.Get("tray.err.hotkeySecond"), true);
        }
        var failed = new List<string>();
        void Register(int id, string text)
        {
            if (!HotkeySpec.TryParse(text, out var spec)) return;   // 未設定
            if (!_hotkeys.Register(id, spec)) failed.Add(spec.ToString());
        }
        Register(HkStart, _settings.HotkeyStart);
        Register(HkPause, _settings.HotkeyPause);
        Register(HkSplit, _settings.HotkeySplit);
        ShowHotkeyInfo(true);
        return failed.Count == 0 ? ("", false) :
            (Loc.Get("tray.err.hotkeyFailed", string.Join("、", failed)), true);
    }

    /// <summary>オプション欄の最下段に、いまのホットキーを薄い文字で出す（ホットキーがONで、1つ以上割り当てがあるときだけ）。</summary>
    private void ShowHotkeyInfo(bool on)
    {
        var items = new List<string>();
        void Add(string label, string text)
        {
            var t = HotkeySpec.Format(text);
            if (t.Length > 0) items.Add($"{label} {t}");
        }
        Add(Loc.Get("hotkey.start"), _settings.HotkeyStart);
        Add(Loc.Get("hotkey.pause"), _settings.HotkeyPause);
        Add(Loc.Get("hotkey.split"), _settings.HotkeySplit);
        HotkeyInfo.Text = Loc.Get("tray.hotkeyInfo", string.Join("　／　", items));
        HotkeyInfo.Visibility = on && items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DisposeTray()
    {
        try { _hotkeys?.Dispose(); } catch (Exception) { }
        try { _tray?.Dispose(); } catch (Exception) { }
        try { _msg?.Dispose(); } catch (Exception) { }
    }
}
