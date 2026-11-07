using System.Windows.Threading;
using NAudio.Wave;

namespace Recorder.Core.Win;

/// <summary>
/// NAudio(AsioOut)による ASIO 入力（M8）。Windows専用。選んだ入力チャンネルだけを録る（出力は使わない）。
/// ・ASIOドライバは COM なので、専用のSTAスレッド（Dispatcher付き）の中で開いて閉じる。
/// ・ドライバは1プロセスに1つしか開けない前提で、同時に開けるのは1つだけ（静的なゲート）。
/// ・ドライバを開く処理はコンストラクターで行う（Format が決まるため）。開けなければ例外。
/// </summary>
public sealed class AsioSource : IAudioSource
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly int[] Rates = { 48000, 44100, 96000, 88200, 192000, 176400 };

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly int _count;
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private AsioOut? _asio;
    private Exception? _openError;
    private float[] _buf = Array.Empty<float>();
    private volatile bool _playing;
    private volatile bool _closing;
    private bool _holdsGate;
    private bool _disposed;

    public AudioFormatInfo Format { get; private set; } = new(48000, 2, 32, true);

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    /// <summary>一覧のためにドライバを一瞬だけ開くとき（AsioDeviceCatalog）の、使用中チェック。使えたら true。</summary>
    internal static bool TryEnter() => Gate.Wait(0);

    internal static void Exit() => Gate.Release();

    public AsioSource(string driver, int firstChannel, int channels)
    {
        _count = channels;
        // 入力チェックなどで開いていた直前のASIOが閉じるのを、少し待つ
        if (!Gate.Wait(TimeSpan.FromSeconds(4)))
            throw new InvalidOperationException("ASIOドライバが使用中です（同時に使えるASIOは1つだけです）。");
        _holdsGate = true;
        try
        {
            _thread = new Thread(() => Run(driver, firstChannel)) { IsBackground = true, Name = "ASIO" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("ASIOドライバが応答しません。");
            if (_openError != null)
                throw new InvalidOperationException(_openError.Message, _openError);
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    /// <summary>専用スレッドの本体。開く → 終了の合図が来るまでメッセージを処理 → 閉じる。</summary>
    private void Run(string driver, int firstChannel)
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            OpenDriver(driver, firstChannel);
        }
        catch (Exception ex)
        {
            _openError = ex;
        }
        finally
        {
            _ready.Set();
        }
        if (_openError != null) return;
        try { Dispatcher.Run(); }
        catch (Exception) { }
        Cleanup();
    }

    /// <summary>サンプリング周波数は 48k → 44.1k → 96k … の順に、ドライバが受け付けるものを使う。</summary>
    private void OpenDriver(string driver, int firstChannel)
    {
        Exception? last = null;
        string? shortage = null;
        foreach (var rate in Rates)
        {
            AsioOut? asio = null;
            try
            {
                asio = new AsioOut(driver);
                int available = asio.DriverInputChannelCount;
                if (firstChannel + _count > available)
                {
                    shortage = $"ASIOドライバ「{driver}」の入力チャンネルが足りません（入力は {available} ch）。";
                    throw new InvalidOperationException(shortage);
                }
                asio.InputChannelOffset = firstChannel;
                asio.InitRecordAndPlayback(null, _count, rate);
                asio.AudioAvailable += OnAudio;
                asio.PlaybackStopped += OnPlaybackStopped;
                _asio = asio;
                Format = new AudioFormatInfo(rate, _count, 32, true);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                try { asio?.Dispose(); } catch (Exception) { }
                if (shortage != null) break;
            }
        }
        if (shortage != null) throw new InvalidOperationException(shortage);
        throw new InvalidOperationException($"ASIOドライバ「{driver}」を開けませんでした（{last?.Message}）", last);
    }

    /// <summary>
    /// ドライバのスレッドから呼ばれる。インターリーブの float にして渡す。
    /// ここで例外が出るとアプリごと落ちるので、必ず捕まえて「止まった」として知らせる（未対応のサンプル形式など）。
    /// </summary>
    private void OnAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        if (!_playing) return;
        try
        {
            int inCh = e.InputBuffers.Length;
            int frames = e.SamplesPerBuffer;
            if (inCh <= 0 || frames <= 0) return;
            int n = frames * inCh;
            if (_buf.Length < n) _buf = new float[n];
            e.GetAsInterleavedSamples(_buf);
            var bytes = new byte[frames * _count * sizeof(float)];
            if (inCh == _count)
            {
                Buffer.BlockCopy(_buf, 0, bytes, 0, bytes.Length);
            }
            else
            {
                // ドライバから届いたチャンネル数が想定と違うときは、先頭から詰める（捨てずに録る）
                var tmp = new float[frames * _count];
                for (int i = 0; i < frames; i++)
                    for (int c = 0; c < _count; c++)
                        tmp[i * _count + c] = c < inCh ? _buf[i * inCh + c] : 0f;
                Buffer.BlockCopy(tmp, 0, bytes, 0, bytes.Length);
            }
            DataAvailable?.Invoke(bytes, bytes.Length);
        }
        catch (Exception ex)
        {
            _playing = false;
            Stopped?.Invoke(new InvalidOperationException("ASIOの音声を読み取れませんでした（" + ex.Message + "）", ex));
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_closing) return;
        _playing = false;
        Stopped?.Invoke(e.Exception ?? new InvalidOperationException("ASIOドライバが停止しました。"));
    }

    public void Start()
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("ASIOドライバが開かれていません。");
        Exception? error = null;
        dispatcher.Invoke(() =>
        {
            try
            {
                _playing = true;
                _asio!.Play();
            }
            catch (Exception ex)
            {
                _playing = false;
                error = ex;
            }
        });
        if (error != null)
            throw new InvalidOperationException("ASIOの録音を始められませんでした（" + error.Message + "）", error);
    }

    public void Stop()
    {
        if (!_playing) return;
        _playing = false;
        _closing = true;
        try
        {
            _dispatcher?.Invoke(() =>
            {
                try { _asio?.Stop(); }
                catch (Exception) { }
            });
        }
        catch (Exception) { }
    }

    /// <summary>専用スレッドの中で、ドライバを閉じる。</summary>
    private void Cleanup()
    {
        var asio = _asio;
        _asio = null;
        if (asio == null) return;
        try
        {
            asio.AudioAvailable -= OnAudio;
            asio.PlaybackStopped -= OnPlaybackStopped;
        }
        catch (Exception) { }
        if (_playing)
        {
            _playing = false;
            try { asio.Stop(); } catch (Exception) { }
        }
        try { asio.Dispose(); } catch (Exception) { }
    }

    /// <summary>専用スレッドに終了を伝えて待ち、ゲートを手放す。何度呼んでもよい。</summary>
    private void Shutdown()
    {
        _closing = true;
        try { _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal); }
        catch (Exception) { }
        try { _thread?.Join(TimeSpan.FromSeconds(5)); }
        catch (Exception) { }
        if (_holdsGate)
        {
            _holdsGate = false;
            Gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
        _ready.Dispose();
    }
}
