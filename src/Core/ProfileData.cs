using System.Text.Encodings.Web;
using System.Text.Json;

namespace Recorder.Core;

/// <summary>プロファイル内のデバイス1台分。</summary>
public sealed class ProfileInput
{
    public string Key { get; set; } = "";
    public double GainDb { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>プロファイル内の、入力1つからの送り。</summary>
public sealed class ProfileSend
{
    public bool On { get; set; } = true;
    public double Db { get; set; }
}

/// <summary>プロファイル内の出力カード1枚分（M11-3。「出力する」のON/OFFは含めない）。</summary>
public sealed class ProfileOutput
{
    public string Name { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public double VolumeDb { get; set; }
    public List<ProfileSend> Sends { get; set; } = new();

    /// <summary>この出力の最終ミックスのエフェクト（M11-5。ローカット・EQ・リミッター）。null＝かけない（旧プロファイルも同じ）。</summary>
    public OutputFxSetting? Fx { get; set; }
}

/// <summary>
/// プロファイル1件。音の設定一式（形式・音質・圧縮率・入力デバイスとゲイン・ミックス/別ファイル・自動分割・
/// ファイル名テンプレート・日付フォルダ・タグ）。保存先フォルダと番号（No/Track）は含めない。
/// 項目が足りない JSON でも読めるよう、すべて既定値つき（PROJECT.md §3-6）。
/// </summary>
public sealed class ProfileData
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "flac";
    public int Bitrate { get; set; } = 192;
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int Compression { get; set; } = -1;
    public List<ProfileInput> Inputs { get; set; } = new();
    public bool SeparateFiles { get; set; }
    public int SplitSeconds { get; set; }
    public string Template { get; set; } = NameTemplates.DefaultName;
    public string Pattern { get; set; } = FileNamer.DefaultPattern;
    public bool DateFolder { get; set; } = true;
    public string DateFolderFormat { get; set; } = FileNamer.DefaultDateFormat;
    public TagInfo Tags { get; set; } = TagInfo.Empty;

    /// <summary>出力カード（M11-3）。null＝この項目が無い旧プロファイル（読み込んでも、いまの出力設定は変えない）。</summary>
    public List<ProfileOutput>? Outputs { get; set; }

    /// <summary>入力の名前（入力1〜4。空＝既定）。null＝旧プロファイル（いまの名前を変えない）。</summary>
    public List<string>? InputNames { get; set; }

    /// <summary>入力ごとのエフェクト（M11-4。入力1〜4。「録音に反映」「出力に反映」を含む）。null＝旧プロファイル（いまの設定を変えない）。</summary>
    public List<EffectSetting>? InputEffects { get; set; }
}

/// <summary>プロファイルと JSON の変換。WPF には依存しない（§6-3）。</summary>
public static class ProfileJson
{
    private static readonly JsonSerializerOptions Compact = Make(false);
    private static readonly JsonSerializerOptions Pretty = Make(true);

    private static JsonSerializerOptions Make(bool indented) => new()
    {
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 日本語をそのまま書く
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>ini に1行で入れる用。</summary>
    public static string ToLine(ProfileData p) => JsonSerializer.Serialize(p, Compact);

    /// <summary>ファイルへ書き出す用（読みやすい形）。</summary>
    public static string ToFile(ProfileData p) => JsonSerializer.Serialize(p, Pretty);

    /// <summary>JSON（1件、または複数件の配列）を読む。失敗したら null と理由。</summary>
    public static List<ProfileData>? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            var text = json.TrimStart('﻿', ' ', '\r', '\n', '\t');
            var list = text.StartsWith('[')
                ? JsonSerializer.Deserialize<List<ProfileData>>(text, Compact)
                : new List<ProfileData?> { JsonSerializer.Deserialize<ProfileData>(text, Compact) }.OfType<ProfileData>().ToList();
            if (list == null || list.Count == 0) { error = Loc.Get("profiledata.s01"); return null; }
            foreach (var p in list) p.Name = p.Name.Trim();
            if (list.Any(p => p.Name.Length == 0)) { error = Loc.Get("profiledata.s02"); return null; }
            return list;
        }
        catch (JsonException ex)
        {
            error = Loc.Get("profiledata.s03") + ex.Message;
            return null;
        }
    }
}
