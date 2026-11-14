namespace Recorder.Core.Win;

/// <summary>
/// アプリが使うデバイス提供元。WASAPI（マイク・ループバック）に、設定で表示をONにしたときだけ ASIO を足す（M8）。Windows専用。
/// 一覧の並び: マイク → スピーカー → ASIO。Key が "asio:" で始まるものは ASIO、それ以外は WASAPI。
/// </summary>
public sealed class AppDeviceProvider : IAudioDeviceProvider
{
    private readonly WasapiDeviceProvider _wasapi = new();
    private readonly Func<bool> _asioEnabled;

    /// <param name="asioEnabled">ASIOを一覧に出すか（設定の「ASIOを表示する」。既定OFF）。呼ぶたびに最新の設定を返すこと。</param>
    public AppDeviceProvider(Func<bool> asioEnabled)
    {
        _asioEnabled = asioEnabled;
    }

    public IReadOnlyList<AudioDeviceInfo> List()
    {
        var list = _wasapi.List().ToList();
        if (_asioEnabled()) list.AddRange(AsioDeviceCatalog.List());
        return list;
    }

    public string? DefaultCaptureKey() => _wasapi.DefaultCaptureKey();

    public string? DefaultRenderKey() => _wasapi.DefaultRenderKey();

    /// <summary>WASAPIの組み合わせに、ASIOの表示のON/OFFとドライバ名を足す（変わったら一覧を読み直す）。</summary>
    public string Signature()
    {
        var sig = _wasapi.Signature();
        return _asioEnabled() ? sig + "#asio:" + string.Join(';', AsioDeviceCatalog.Drivers()) : sig + "#noasio";
    }

    public IAudioSource Create(string key)
    {
        if (AsioKey.Is(key))
        {
            if (!AsioKey.TryParse(key, out var driver, out var first, out var count))
                throw new ArgumentException((Loc.Get("appdeviceprovider.s01") + " ") + key);
            return new AsioSource(driver, first, count);
        }
        return _wasapi.Create(key);
    }
}
