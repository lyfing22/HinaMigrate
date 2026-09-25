using System.Text;
using System.Text.Json;
using DataMigrate.Infrastructure;
using DataMigrate.Sidecar.Ipc;

// stdout 用 UTF-8，避免 CJK 乱码
try { Console.OutputEncoding = Encoding.UTF8; } catch { }
try { Console.InputEncoding = Encoding.UTF8; } catch { }

// 1. 捕获真实 stdout/stderr（之后 Console.Out 会被重定向）
var realStdout = Console.Out;
var realStderr = Console.Error;

// 2. IPC 写入器（所有对外输出经此走 stdout）
var ipc = new IpcWriter(realStdout, realStderr);

// 3. 重定向 Console.Out：把残留的 Console.Write 包裹为 console 事件
Console.SetOut(new ConsoleRedirector(ipc));

// 4. Serilog：IPC sink（stdout JSON）+ 文件 sink（滚动日志）
var logDir = Environment.GetEnvironmentVariable("FJYXHR_LOG_DIR")
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
var logger = LogHelper.CreateIpcLogger(ipc.EmitRaw, logDir);

logger.Information("Sidecar 启动，logDir={LogDir}", logDir);

var host = new SidecarHost(ipc, logger, logDir);

// 5. stdin 命令循环：每行一个 JSON 请求
var stdin = Console.In;
string? line;
while ((line = await stdin.ReadLineAsync()) is not null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;

    IpcRequest? req = null;
    try
    {
        req = JsonSerializer.Deserialize<IpcRequest>(line, IpcRequest.ParseOpts);
    }
    catch (Exception ex)
    {
        ipc.EmitError("", $"请求解析失败: {ex.Message}; raw={line}");
        continue;
    }

    if (req is null || string.IsNullOrWhiteSpace(req.Cmd))
    {
        ipc.EmitError("", "请求缺少 cmd 字段");
        continue;
    }

    await host.HandleAsync(req);
}

logger.Information("stdin 关闭，Sidecar 退出");
host.Dispose();
await logger.DisposeAsync();
