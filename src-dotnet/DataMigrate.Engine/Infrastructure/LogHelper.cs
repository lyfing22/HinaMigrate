using Serilog;

namespace DataMigrate.Infrastructure;

public static class LogHelper
{
    /// <summary>控制台模式 logger（Console + File）。桌面 Sidecar 不使用。</summary>
    public static Serilog.Core.Logger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/migrate-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// 桌面 Sidecar 专用 logger：日志经 <see cref="IpcLogSink"/> 以单行 JSON 写入 stdout
    /// （由 emitLine 回传给 IpcWriter），同时落盘到指定目录。不写 Console，避免污染 stdout JSON 流。
    /// </summary>
    public static Serilog.Core.Logger CreateIpcLogger(Action<string> emitLine, string logDir)
    {
        Directory.CreateDirectory(logDir);
        var file = Path.Combine(logDir, "migrate-.log");

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new IpcLogSink(emitLine))
            .WriteTo.File(file,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
