using System.Text.Json;
using System.Text.Encodings.Web;

namespace DataMigrate.Sidecar.Ipc;

/// <summary>
/// 线程安全的 stdout JSON 行写入器。Sidecar 所有对外输出（进度/日志/结果/错误）
/// 都经此写入，保证 stdout 每行均为合法 JSON。
/// </summary>
public sealed class IpcWriter
{
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions EmitOpts = new()
    {
        // 对外统一 camelCase，便于前端 TypeScript 消费
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public IpcWriter(TextWriter @out, TextWriter err)
    {
        _out = @out;
        _err = err;
    }

    /// <summary>序列化对象为 JSON 行写入 stdout（带 flush）。</summary>
    public void Emit(object payload)
    {
        var json = JsonSerializer.Serialize(payload, EmitOpts);
        lock (_lock)
        {
            _out.WriteLine(json);
            _out.Flush();
        }
    }

    /// <summary>直接写入已序列化的 JSON 行。</summary>
    public void EmitRaw(string jsonLine)
    {
        lock (_lock)
        {
            _out.WriteLine(jsonLine);
            _out.Flush();
        }
    }

    /// <summary>错误事件：同时写 stdout 与 stderr，便于 Rust 两侧捕获。</summary>
    public void EmitError(string? id, string message)
    {
        var payload = new { id, type = "error", message };
        var json = JsonSerializer.Serialize(payload, EmitOpts);
        lock (_lock)
        {
            _out.WriteLine(json);
            _out.Flush();
            _err.WriteLine(json);
            _err.Flush();
        }
    }
}
