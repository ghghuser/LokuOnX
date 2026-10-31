using System.Security.Cryptography;
using System.Text;

namespace Recorder.Core;

/// <summary>
/// 多重起動の判定。「同じフォルダのexe」だけを同一とみなす（PROJECT.md §3-6）。
/// 別フォルダのコピーは設定ファイルも別なので、警告せず普通に起動できる。
/// </summary>
public static class InstanceGuard
{
    private static Mutex? _mutex;

    /// <summary>true なら最初の起動。false なら同じフォルダで既に起動中。</summary>
    public static bool TryAcquire(string exeDir)
    {
        var norm = Path.GetFullPath(exeDir).TrimEnd('\\', '/').ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
        _mutex = new Mutex(true, $@"Local\{AppInfo.Name}_{hash}", out var createdNew);
        return createdNew;
    }
}
