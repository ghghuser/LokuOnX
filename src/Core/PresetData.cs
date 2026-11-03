using System.Text.Encodings.Web;
using System.Text.Json;

namespace Recorder.Core;

/// <summary>プリセット内のデバイス1台分。</summary>
public sealed class PresetInput
{
    public string Key { get; set; } = "";
    public double GainDb { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// プリセット1件。音の設定一式（形式・音質・圧縮率・入力デバイスとゲイン・ミックス/別ファイル・自動分割・
/// ファイル名テンプレート・日付フォルダ・タグ）。保存先フォルダと番号（No/Track）は含めない。
/// 項目が足りない JSON でも読めるよう、すべて既定値つき（PROJECT.md §3-6）。
/// </summary>
public sealed class PresetData
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "flac";
    public int Bitrate { get; set; } = 192;
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int Compression { get; set; } = -1;
    public List<PresetInput> Inputs { get; set; } = new();
    public bool SeparateFiles { get; set; }
    public int SplitSeconds { get; set; }
    public string Template { get; set; } = NameTemplates.DefaultName;
    public string Pattern { get; set; } = FileNamer.DefaultPattern;
    public bool DateFolder { get; set; } = true;
    public string DateFolderFormat { get; set; } = FileNamer.DefaultDateFormat;
    public TagInfo Tags { get; set; } = TagInfo.Empty;
}

/// <summary>プリセットと JSON の変換。WPF には依存しない（§6-3）。</summary>
public static class PresetJson
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
    public static string ToLine(PresetData p) => JsonSerializer.Serialize(p, Compact);

    /// <summary>ファイルへ書き出す用（読みやすい形）。</summary>
    public static string ToFile(PresetData p) => JsonSerializer.Serialize(p, Pretty);

    /// <summary>JSON（1件、または複数件の配列）を読む。失敗したら null と理由。</summary>
    public static List<PresetData>? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            var text = json.TrimStart('﻿', ' ', '\r', '\n', '\t');
            var list = text.StartsWith('[')
                ? JsonSerializer.Deserialize<List<PresetData>>(text, Compact)
                : new List<PresetData?> { JsonSerializer.Deserialize<PresetData>(text, Compact) }.OfType<PresetData>().ToList();
            if (list == null || list.Count == 0) { error = "プリセットが入っていません。"; return null; }
            foreach (var p in list) p.Name = p.Name.Trim();
            if (list.Any(p => p.Name.Length == 0)) { error = "名前のないプリセットがあります。"; return null; }
            return list;
        }
        catch (JsonException ex)
        {
            error = "JSONを読めませんでした: " + ex.Message;
            return null;
        }
    }
}
