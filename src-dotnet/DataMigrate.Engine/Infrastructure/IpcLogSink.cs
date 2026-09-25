using System.Text.Json;
using Serilog.Core;
using Serilog.Events;

namespace DataMigrate.Infrastructure;

/// <summary>
/// Serilog 自定义 sink：将每条日志渲染为单行 JSON `{"type":"log",...}` 并通过
/// <paramref name="emitLine"/> 回传给 Sidecar 的 IpcWriter，最终写入 stdout。
/// 与 IpcWriter 共同保证 stdout 每行均为合法 JSON。
/// </summary>
public sealed class IpcLogSink : ILogEventSink
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Action<string> _emitLine;

    public IpcLogSink(Action<string> emitLine) => _emitLine = emitLine;

    public void Emit(LogEvent logEvent)
    {
        try
        {
            var msg = logEvent.RenderMessage();
            var payload = new LogPayload
            {
                Type = "log",
                Level = MapLevel(logEvent.Level),
                Message = msg,
                Ts = logEvent.Timestamp.LocalDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                Error = logEvent.Exception?.Message
            };
            _emitLine(JsonSerializer.Serialize(payload, JsonOpts));
        }
        catch
        {
            // IPC 通道异常不应拖垮迁移主流程
        }
    }

    private static string MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "FTL",
        _ => "INF"
    };

    private sealed class LogPayload
    {
        public string Type { get; set; } = "";
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string Ts { get; set; } = "";
        public string? Error { get; set; }
    }
}
