using System.Text.Json;
using DataMigrate.Core;

namespace DataMigrate.Sidecar.Ipc;

/// <summary>来自前端的 IPC 请求（stdin 每行一个）。</summary>
public sealed class IpcRequest
{
    public string Id { get; set; } = "";
    public string Cmd { get; set; } = "";
    /// <summary>命令参数（按命令解析）。大小写不敏感反序列化。</summary>
    public JsonElement? Args { get; set; }

    public static readonly JsonSerializerOptions ParseOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>从 Args 中解析出 MigrationOptions（profile）。</summary>
    public MigrationOptions GetProfile()
    {
        if (Args is null || Args.Value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("缺少 args.profile");
        if (Args.Value.TryGetProperty("profile", out var p))
            return p.Deserialize<MigrationOptions>(ParseOpts)
                   ?? throw new InvalidOperationException("profile 解析失败");
        // 兼容：直接把整个 args 当作 profile
        return Args.Value.Deserialize<MigrationOptions>(ParseOpts)
               ?? throw new InvalidOperationException("profile 解析失败");
    }

    public string GetArgString(string key)
    {
        if (Args is null) return "";
        return Args.Value.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
    }

    public string[]? GetArgStringArray(string key)
    {
        if (Args is null) return null;
        if (!Args.Value.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        return v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
    }
}
