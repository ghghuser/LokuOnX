using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Recorder.Core;

/// <summary>エフェクトのプリセット1件（効果の中身だけ。「録音に反映」「出力に反映」は含めない）。</summary>
public sealed class EffectPreset
{
    public string Name { get; set; } = "";
    public EffectSetting Sound { get; set; } = new();
}

/// <summary>
/// エフェクトのユーザープリセット（M16）。入力のエフェクトと出力のエフェクトで共通の一覧。
/// exe と同じ場所の EffectPresets フォルダに「名前.json」で1件ずつ保存する（コピーで共有・バックアップできる）。
/// 初回（フォルダが無いとき）だけ既定のプリセットを書き出す。既定のものも削除・上書きでき、「初期プリセットを復元」で戻せる。
/// 2つ目の起動（書き込まない側）では書かない。
/// </summary>
public sealed class EffectPresetStore
{
    public const int MaxNameLength = 40;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>ファイル名に使えない文字（Windows の規則。Linux で動かしても同じ結果になるよう固定）。</summary>
    private const string InvalidChars = "\\/:*?\"<>|";

    private readonly string dir;
    private readonly bool readOnly;

    public EffectPresetStore(string folder, bool readOnly)
    {
        dir = folder;
        this.readOnly = readOnly;
    }

    /// <summary>プリセットのJSONを入れるフォルダ。</summary>
    public string Folder => dir;

    /// <summary>直近の保存エラー（なければ null）。</summary>
    public string? Error { get; private set; }

    /// <summary>名前を、保存できる形（前後の空白なし・禁止文字なし・最大40文字）に整える。空なら空文字。</summary>
    public static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in (name ?? "").Trim())
            sb.Append(ch < 32 || InvalidChars.Contains(ch) ? '＿' : ch);
        var s = sb.ToString().Trim().TrimEnd('.');
        return s.Length > MaxNameLength ? s[..MaxNameLength].Trim() : s;
    }

    private string FileOf(string name) => Path.Combine(dir, CleanName(name) + ".json");

    /// <summary>保存されているプリセット（名前順）。読めないファイルは飛ばす。</summary>
    public IReadOnlyList<EffectPreset> List()
    {
        var list = new List<EffectPreset>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<EffectPreset>(File.ReadAllText(file, Encoding.UTF8), Opts);
                    if (p == null || CleanName(p.Name).Length == 0) continue;
                    p.Name = CleanName(p.Name);
                    p.Sound ??= new EffectSetting();
                    p.Sound.Normalize();
                    list.Add(p);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>名前（大文字小文字は区別しない）のプリセット。無ければ null。</summary>
    public EffectPreset? Get(string name)
    {
        var n = CleanName(name);
        return n.Length == 0 ? null : List().FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>同じ名前があれば上書き、なければ追加する。成功したら true。書き込まない側の起動・名前が空のときは false。</summary>
    public bool Save(string name, EffectSetting fx)
    {
        var n = CleanName(name);
        if (readOnly || n.Length == 0) return false;
        var sound = fx.Clone();
        sound.Bypass = false; sound.ToRecording = false; sound.ToOutput = true;   // 効果の中身だけ
        sound.Normalize();
        return Write(new EffectPreset { Name = n, Sound = sound });
    }

    private bool Write(EffectPreset p)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = FileOf(p.Name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(p, Opts), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            Error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            return false;
        }
    }

    public void Delete(string name)
    {
        if (readOnly) return;
        try { File.Delete(FileOf(name)); Error = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = ex.Message; }
    }

    /// <summary>フォルダが無い（初回）ときだけ、既定のプリセットを書き出す。</summary>
    public void EnsureDefaults()
    {
        if (readOnly) return;
        try { if (Directory.Exists(dir)) return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        RestoreDefaults();
    }

    /// <summary>既定のプリセットを書き出す（同じ名前のものは上書き。ほかのプリセットには触らない）。</summary>
    public void RestoreDefaults()
    {
        if (readOnly) return;
        foreach (var p in Defaults()) Write(p);
    }

    /// <summary>最初から用意するプリセット。名前は保存名になるため、言語を変えても変わらない固定の文字列。</summary>
    public static IReadOnlyList<EffectPreset> Defaults() => new[]
    {
        new EffectPreset
        {
            Name = "ボーカル低減（カラオケ）",
            Sound = new EffectSetting { VocalOn = true, VocalAmount = 0.9, VocalLowHz = 200, VocalHighHz = 6000 },
        },
        new EffectPreset
        {
            Name = "声をはっきり",
            // 31/62/125/250/500/1k/2k/4k/8k/16kHz。低い響きを少し減らし、2〜4kHzを持ち上げる。ローカット100Hz＋軽いリミッター
            Sound = new EffectSetting
            {
                HpfHz = 100, EqOn = true, Bands = new double[] { 0, 0, 0, -2, -1, 1, 3, 3, 1, 0 },
                LimiterOn = true, CeilingDb = -2, ReleaseMs = 150,
            },
        },
        new EffectPreset
        {
            Name = "ノイズ除去",
            Sound = new EffectSetting { GateOn = true, GateThresholdDb = -45, GateReleaseMs = 200, GateReductionDb = 40, HpfHz = 100 },
        },
        new EffectPreset
        {
            Name = "音量そろえ（リミッター）",
            Sound = new EffectSetting { LimiterOn = true, CeilingDb = -6, ReleaseMs = 150 },
        },
    };
}
