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
            Hotkeys.CaptureChanged += capturing =>
            {
                if (capturing) _hotkeys?.UnregisterAll();   // 割り当て中は、登録済みのキーを外す（押したキーを受け取るため）
                else ApplyHotkeys();
            };
        }
        catch (Exception ex)
        {
            _tray = null;
            _hotkeys = null;
            StatusText.Text = "タスクトレイ・ホットキーを準備できませんでした: " + ex.Message;
        }
        ApplyTray();
        ApplyHotkeys();
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
        var start = rec ? "録音を停止" : _standby ? "待機をやめる" : _settings.AutoStart ? "録音待機を始める" : "録音を開始";
        return new[]
        {
            new TrayMenuItem(CmdOpen, $"{AppInfo.Name} を表示"),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdStart, start, StartButton.IsEnabled),
            new TrayMenuItem(CmdPause, _session?.Paused == true ? "再開" : "一時停止", rec && PauseButton.IsEnabled),
            new TrayMenuItem(CmdSplit, "分割", rec && SplitButton.IsEnabled),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdExit, "終了"),
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

    /// <summary>ホットキーを登録し直す。ほかのアプリが使っているキーなどで登録できなかったものは、オプション欄に赤文字で出す。</summary>
    private void ApplyHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.UnregisterAll();
        if (!_settings.HotkeysOn) { Hotkeys.SetMessage("", false); return; }
        if (_settings.ReadOnly)
        {
            Hotkeys.SetMessage("2つ目の起動ではホットキーは使えません（最初に起動したほうで使います）。", true);
            return;
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
        Hotkeys.SetMessage(failed.Count == 0 ? "" :
            "登録できなかったキーがあります（ほかのアプリが使っています）: " + string.Join("、", failed) + "。別のキーに変えてください。", true);
    }

    private void DisposeTray()
    {
        try { _hotkeys?.Dispose(); } catch (Exception) { }
        try { _tray?.Dispose(); } catch (Exception) { }
        try { _msg?.Dispose(); } catch (Exception) { }
    }
}
