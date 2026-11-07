using NAudio.Wave;

namespace Recorder.Core.Win;

/// <summary>
/// インストール済みのASIOドライバを、入力チャンネルの組み合わせ（ステレオ「入力1-2」「入力3-4」…と、モノ「入力1」「入力2」…）として一覧にする（M8）。Windows専用。
/// ドライバの入力チャンネル数は、ドライバを一瞬だけ開いて調べる（成功した結果は起動中ずっと覚える。使用中・失敗のときはステレオ1組だけにして、30秒は調べ直さない）。
/// </summary>
public static class AsioDeviceCatalog
{
    /// <summary>一覧に出すチャンネル数の上限（多チャンネルのインターフェースで、一覧が長くなりすぎないように）。</summary>
    public const int MaxListedChannels = 16;

    private const int FallbackChannels = 2;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private static readonly object Lock = new();
    private static readonly Dictionary<string, int> Known = new();
    private static readonly Dictionary<string, DateTime> FailedAt = new();

    /// <summary>インストールされているASIOドライバの名前（レジストリを読むだけ。軽い）。名前順。</summary>
    public static string[] Drivers()
    {
        try
        {
            var names = AsioOut.GetDriverNames();
            if (names == null) return Array.Empty<string>();
            return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>一覧に出すASIOの入力。ドライバ名順に、ステレオの組 → モノの順。</summary>
    public static IEnumerable<AudioDeviceInfo> List()
    {
        var result = new List<AudioDeviceInfo>();
        foreach (var driver in Drivers())
        {
            int n = Math.Min(InputChannels(driver), MaxListedChannels);
            for (int first = 0; first + 1 < n; first += 2) result.Add(Item(driver, first, 2));
            for (int first = 0; first < n; first++) result.Add(Item(driver, first, 1));
        }
        return result;
    }

    private static AudioDeviceInfo Item(string driver, int first, int count) =>
        new(AsioKey.Make(driver, first, count), $"🎛 {driver} 入力{AsioKey.ChannelText(first, count)}",
            false, 0, count);

    private static int InputChannels(string driver)
    {
        lock (Lock)
        {
            if (Known.TryGetValue(driver, out var known)) return known;
            if (FailedAt.TryGetValue(driver, out var at) && DateTime.UtcNow - at < RetryAfter) return FallbackChannels;
        }
        // 録音・入力チェックでそのドライバを開いているあいだは、開き直さない（動いている音を乱さないため）
        if (!AsioSource.TryEnter()) return FallbackChannels;
        try
        {
            using var asio = new AsioOut(driver);
            int n = Math.Max(1, asio.DriverInputChannelCount);
            lock (Lock) Known[driver] = n;
            return n;
        }
        catch (Exception)
        {
            lock (Lock) FailedAt[driver] = DateTime.UtcNow;
            return FallbackChannels;
        }
        finally
        {
            AsioSource.Exit();
        }
    }
}
