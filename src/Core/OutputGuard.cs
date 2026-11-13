namespace Recorder.Core;

/// <summary>出力先の検査（M11-2）。WPF や Windows API には依存しない。</summary>
public static class OutputGuard
{
    /// <summary>音が回る組み合わせのときの理由（出力エンジンの赤文字と、出力カードの事前の注意で共通）。</summary>
    public static string LoopMessage => Loc.Get("output.loopMessage");

    /// <summary>
    /// 出力先が、取り込み元（入力）のループバックと同じデバイスか（音が回って鳴り続ける・ハウリングする）。
    /// outputId は "default" か デバイスID、defaultRenderKey は Windows の既定の出力の Key（"render:{ID}"）、
    /// realInputKeys は「既定のデバイス」を実際のデバイスに置き換えた入力の Key。
    /// </summary>
    public static bool IsLoop(string outputId, string? defaultRenderKey, IEnumerable<string> realInputKeys)
    {
        const string prefix = "render:";
        string? outId = outputId == OutputDeviceInfo.DefaultId
            ? (defaultRenderKey != null && defaultRenderKey.StartsWith(prefix, StringComparison.Ordinal) ? defaultRenderKey.Substring(prefix.Length) : null)
            : outputId;
        if (outId == null) return false;
        foreach (var key in realInputKeys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && string.Equals(key.Substring(prefix.Length), outId, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
