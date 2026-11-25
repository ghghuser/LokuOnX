using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recorder.Core;

/// <summary>制御API（M17）の1行ぶんの要求。改行区切りのJSON（1行＝1件）の { "id": 1, "cmd": "rec.start", ...引数 } を読んだもの。</summary>
public sealed class ControlRequest
{
    public long? Id { get; init; }
    public string Cmd { get; init; } = "";

    /// <summary>要求のJSONオブジェクト全体（引数はここから取る）。</summary>
    public JsonElement Args { get; init; }

    private bool Prop(string name, out JsonElement value)
    {
        value = default;
        return Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out value);
    }

    public bool TryGetInt(string name, out int value)
    {
        value = 0;
        return Prop(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value);
    }

    public bool TryGetDouble(string name, out double value)
    {
        value = 0;
        return Prop(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value) && double.IsFinite(value);
    }

    public bool TryGetBool(string name, out bool value)
    {
        value = false;
        if (!Prop(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.True) { value = true; return true; }
        return p.ValueKind == JsonValueKind.False;
    }

    public bool TryGetString(string name, out string value)
    {
        value = "";
        if (!Prop(name, out var p) || p.ValueKind != JsonValueKind.String) return false;
        value = p.GetString() ?? "";
        return true;
    }
}

/// <summary>制御API（M17）の書式。1行＝1件のJSON（UTF-8、改行は \n）。要求は { "id":任意の数, "cmd":"名前", 引数… }、応答は { "id":…, "ok":true, "data":… } か { "id":…, "ok":false, "error":{ "code":…, "message":… } }、通知は { "event":"state", "data":… }。</summary>
public static class ControlProtocol
{
    /// <summary>API の版（互換のない変更をしたら上げる）。</summary>
    public const int Version = 1;

    /// <summary>1行の最大長（これを超えたら接続を切る）。</summary>
    public const int MaxLineChars = 65536;

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>名前付きパイプの名前。「同じフォルダのexe」ごとに別（多重起動の判定と同じ考え方）。</summary>
    public static string PipeName(string exeDir, string appName)
    {
        var norm = Path.GetFullPath(exeDir).TrimEnd('\\', '/').ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
        return $"{appName}.{hash}";
    }

    /// <summary>1行を要求として読む。読めなければ false（error に理由）。</summary>
    public static bool TryParse(string line, out ControlRequest request, out string error)
    {
        request = new ControlRequest();
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "request must be a JSON object"; return false; }
            long? id = null;
            if (root.TryGetProperty("id", out var idEl))
            {
                if (idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt64(out var n)) { error = "id must be an integer"; return false; }
                id = n;
            }
            if (!root.TryGetProperty("cmd", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(cmdEl.GetString()))
            {
                error = "cmd is required";
                return false;
            }
            request = new ControlRequest { Id = id, Cmd = cmdEl.GetString()!, Args = root.Clone() };
            return true;
        }
        catch (JsonException)
        {
            error = "invalid JSON";
            return false;
        }
    }

    public static string Ok(long? id, object? data = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = id, ["ok"] = true, ["data"] = data }, Options);

    public static string Fail(long? id, string code, string message) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id, ["ok"] = false, ["error"] = new Dictionary<string, object?> { ["code"] = code, ["message"] = message },
        }, Options);

    public static string Event(string name, object data) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["event"] = name, ["data"] = data }, Options);
}
