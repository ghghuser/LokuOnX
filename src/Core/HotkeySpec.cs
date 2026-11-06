namespace Recorder.Core;

/// <summary>
/// グローバルホットキー1つ分（修飾キー＋キー）。"Ctrl+Alt+F9" のような文字列と相互に変換する（PROJECT.md §3-8）。
/// 修飾キーを1つ以上含むものだけ有効（キー単独だと他のアプリの入力を奪うため）。WPF には依存しない。
/// </summary>
public readonly record struct HotkeySpec(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;   // RegisterHotKey の MOD_* と同じ値

    private static readonly Dictionary<string, uint> Keys = BuildKeys();

    private static Dictionary<string, uint> BuildKeys()
    {
        var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= 24; i++) d["F" + i] = (uint)(0x70 + i - 1);
        for (char c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        d["Space"] = 0x20;
        d["Pause"] = 0x13;
        d["PageUp"] = 0x21;
        d["PageDown"] = 0x22;
        d["End"] = 0x23;
        d["Home"] = 0x24;
        d["Insert"] = 0x2D;
        d["Delete"] = 0x2E;
        return d;
    }

    /// <summary>画面のキー名（"F9" "A" など）に使える名前か。</summary>
    public static bool IsKeyName(string name) => Keys.ContainsKey(name);

    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0, vk = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= Ctrl; break;
                case "alt": mods |= Alt; break;
                case "shift": mods |= Shift; break;
                case "win": mods |= Win; break;
                default:
                    if (vk != 0 || !Keys.TryGetValue(part, out vk)) return false;
                    break;
            }
        }
        if (vk == 0 || mods == 0) return false;
        spec = new HotkeySpec(mods, vk);
        return true;
    }

    /// <summary>"Ctrl+Alt+F9" の形に直す。</summary>
    public static string Format(string? text) =>
        TryParse(text, out var spec) ? spec.ToString() : "";

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        foreach (var kv in Keys)
        {
            if (kv.Value != VirtualKey) continue;
            parts.Add(kv.Key);
            break;
        }
        return string.Join("+", parts);
    }
}
