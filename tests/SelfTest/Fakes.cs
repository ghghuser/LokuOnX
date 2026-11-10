using Recorder.Core;

/// <summary>テスト用の入力デバイス。Feed で float の音を流し込む（デバイスのコールバックの代わり）。</summary>
sealed class FakeSource : IAudioSource
{
    public AudioFormatInfo Format { get; }
    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;
    public int StopCount;

    public FakeSource(int rate, int ch) => Format = new AudioFormatInfo(rate, ch, 32, true);

    public void Start() { }

    public void Stop() => Interlocked.Increment(ref StopCount);

    public void Dispose() { }

    public void Feed(float value, int frames)
    {
        var f = new float[frames * Format.Channels];
        Array.Fill(f, value);
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        DataAvailable?.Invoke(b, b.Length);
    }
}

/// <summary>テスト用の入力デバイス一覧。Create のたびに FakeSource を1つ作って Sources に残す（開いた回数の確認用）。</summary>
sealed class FakeProvider : IAudioDeviceProvider
{
    public readonly List<FakeSource> Sources = new();
    public int Rate = 48000;
    public int Ch = 2;

    public IReadOnlyList<AudioDeviceInfo> List() => Array.Empty<AudioDeviceInfo>();
    public string? DefaultCaptureKey() => "capture:A";
    public string? DefaultRenderKey() => "render:SPK";
    public string Signature() => "";

    public IAudioSource Create(string key)
    {
        var s = new FakeSource(Rate, Ch);
        lock (Sources) Sources.Add(s);
        return s;
    }
}

/// <summary>テスト用の出力デバイス。Renderer を取り出して、デバイスのコールバックの代わりに Render を呼ぶ。</summary>
sealed class FakeOut : IOutputDevice
{
    public readonly string Id;
    public string? StartError;
    public bool Disposed;
    public OutputRenderer? Renderer { get; private set; }
    public bool Failed { get; set; }
    public string LastError { get; set; } = "";

    public FakeOut(string id) => Id = id;

    public string? Start(Func<int, int, OutputRenderer> factory)
    {
        if (StartError != null) return StartError;
        Renderer = factory(48000, 2);
        return null;
    }

    public void Dispose() => Disposed = true;
}

sealed class FakeOutProvider : IOutputDeviceProvider
{
    public readonly List<FakeOut> Devices = new();
    public string? NextStartError;

    public IReadOnlyList<OutputDeviceInfo> List() =>
        new[] { new OutputDeviceInfo(OutputDeviceInfo.DefaultId, "既定"), new OutputDeviceInfo("SPK", "SPK") };

    public IOutputDevice Create(string id)
    {
        var d = new FakeOut(id) { StartError = NextStartError };
        lock (Devices) Devices.Add(d);
        return d;
    }
}
