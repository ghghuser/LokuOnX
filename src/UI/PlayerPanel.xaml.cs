using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>
/// プレイヤー（PROJECT.md §3-7）: 再生／停止／シークバー／速度 0.5〜2倍（音程維持＝FFmpeg の atempo）。
/// 「停止」は位置を覚えているので、もう一度「再生」で続きから始まる。ファイルの最後まで行くと先頭に戻る。
/// </summary>
public partial class PlayerPanel : UserControl
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _seekDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _speedDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private FfmpegPlayer? _player;
    private LibraryFile? _file;
    private double _pos;          // 止まっているときの再生位置（秒）
    private bool _updating;       // 画面側から値を入れているとき（ValueChanged を無視する）
    private bool _dragging;

    public PlayerPanel()
    {
        InitializeComponent();
        PosSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        PosSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _dragging = false;
            ApplySeek();
        }));
        _tick.Tick += (_, _) => OnTick();
        _seekDelay.Tick += (_, _) => ApplySeek();
        _speedDelay.Tick += (_, _) => ApplySpeed();
        SetState();
    }

    public void Init(string ffmpegPath)
    {
        _player = new FfmpegPlayer(ffmpegPath);
        _player.Ended += OnEnded;
        _player.Failed += OnFailed;
    }

    private bool Playing => _player?.IsPlaying == true;
    private double Speed => PlaybackArgs.ClampSpeed(SpeedSlider.Value);
    private double Length => _file?.Length?.TotalSeconds ?? 0;

    // ---- 一覧からの操作 ----
    /// <summary>
    /// 一覧で選んだファイルを、再生の対象にする（再生はしない）。
    /// 再生中は、別のファイルを選んでも止めない（次を再生する／停止するまで続ける）。
    /// </summary>
    public void Load(LibraryFile file)
    {
        if (Playing) return;
        var same = _file != null && string.Equals(_file.Path, file.Path, StringComparison.OrdinalIgnoreCase);
        if (!same)
        {
            _player?.Stop();
            _pos = 0;
        }
        SetFile(file);
        SetState();
    }

    /// <summary>ダブルクリックなどで「このファイルを再生」。再生中でも、これに切り替える（同じファイルを再生中なら何もしない）。</summary>
    public void PlayFile(LibraryFile file)
    {
        var same = _file != null && string.Equals(_file.Path, file.Path, StringComparison.OrdinalIgnoreCase);
        if (same && Playing) return;
        if (!same)
        {
            _player?.Stop();
            _pos = 0;
        }
        SetFile(file);
        Play();
    }

    /// <summary>名前の変更・削除の前に、そのファイルの再生を止めて手放す。</summary>
    public void Release(IReadOnlyCollection<string> paths)
    {
        if (_file == null || !paths.Contains(_file.Path, StringComparer.OrdinalIgnoreCase)) return;
        _player?.Stop();
        SetFile(null);
        _pos = 0;
        SetState();
    }

    public void Play()
    {
        ClearError();
        if (_player == null || _file == null) return;
        if (!File.Exists(_file.Path))
        {
            ShowError(Loc.Get("playerpanel.s01"));
            return;
        }
        if (Length > 0 && _pos >= Length - 0.5) _pos = 0;
        _player.Play(_file.Path, _pos, Speed);
        SetState();
    }

    public void Shutdown()
    {
        _tick.Stop();
        _player?.Dispose();
    }

    // ---- ボタン・スライダー ----
    private void Play_Click(object sender, RoutedEventArgs e) => Play();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        _player.Stop();
        _pos = _player.Position;
        SetState();
    }

    private void Pos_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || TimeText == null) return;
        _pos = PosSlider.Value;
        ShowTime(_pos);
        if (_dragging) return;
        _seekDelay.Stop();   // キーボードなどの変更は、少し待ってから反映（連続操作で起こし直さない）
        _seekDelay.Start();
    }

    private void ApplySeek()
    {
        _seekDelay.Stop();
        if (_player == null || _file == null) return;
        _pos = PosSlider.Value;
        if (Playing) Play();
        else ShowTime(_pos);
    }

    private void Speed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedText == null) return;
        SpeedText.Text = $"{Speed:0.00}×";
        _speedDelay.Stop();
        _speedDelay.Start();
    }

    private void ApplySpeed()
    {
        _speedDelay.Stop();
        if (_player == null || !Playing) return;
        _pos = _player.Position;   // 速度を変えても、いまの位置から続ける
        Play();
    }

    private void SpeedReset_Click(object sender, RoutedEventArgs e) => SpeedSlider.Value = 1.0;

    // ---- 表示 ----
    private void OnTick()
    {
        if (_player == null || !Playing) return;
        var pos = Math.Min(_player.Position, Length > 0 ? Length : double.MaxValue);
        if (_dragging) return;
        _pos = pos;
        SetSlider(pos);
    }

    private void OnEnded()
    {
        _pos = 0;
        SetSlider(0);
        SetState();
    }

    private void OnFailed(string message)
    {
        SetState();
        ShowError(message);
    }

    private void SetFile(LibraryFile? file)
    {
        if (_file != null) _file.PropertyChanged -= OnFileChanged;
        _file = file;
        if (_file != null) _file.PropertyChanged += OnFileChanged;
        ClearError();
        UpdateName();
        SetSlider(_pos);
    }

    private void UpdateName() =>
        NameText.Text = _file == null ? Loc.Get("playerpanel.s02") : (Playing ? Loc.Get("playerpanel.s03") : "") + _file.Name;

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryFile.Length)) { SetSlider(_pos); SetState(); }
    }

    /// <summary>スライダーとボタンの状態。長さが分かるまで（FFmpegで読む間）シークバーは動かせない。</summary>
    private void SetState()
    {
        var has = _file != null;
        PlayButton.IsEnabled = has && !Playing;
        StopButton.IsEnabled = Playing;
        PosSlider.IsEnabled = has && Length > 0;
        _tick.IsEnabled = Playing;
        UpdateName();
    }

    private void SetSlider(double pos)
    {
        _updating = true;
        PosSlider.Maximum = Length > 0 ? Length : 1;
        PosSlider.Value = Math.Clamp(pos, 0, PosSlider.Maximum);
        _updating = false;
        ShowTime(pos);
    }

    private void ShowTime(double pos) =>
        TimeText.Text = $"{LibraryFile.Clock(TimeSpan.FromSeconds(Math.Max(0, pos)))} / {LibraryFile.Clock(TimeSpan.FromSeconds(Length))}";

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
