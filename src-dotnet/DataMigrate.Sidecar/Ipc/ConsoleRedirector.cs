using System.Text;

namespace DataMigrate.Sidecar.Ipc;

/// <summary>
/// 重定向 Console.Out：把引擎中残留的 Console.Write/WriteLine（如
/// MigrationStatistics.PrintSummary 的汇总框）按整行包裹为
/// <c>{"type":"console","text":"..."}</c> 事件，经 IpcWriter 写入 stdout，
/// 确保 stdout 永远是合法的 JSON 行流。Serilog 的日志走 IpcLogSink，不经过此处。
/// </summary>
internal sealed class ConsoleRedirector : TextWriter
{
    private readonly IpcWriter _ipc;
    private readonly StringBuilder _buf = new();
    private readonly object _lock = new();

    public ConsoleRedirector(IpcWriter ipc) => _ipc = ipc;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_lock)
        {
            if (value == '\n' || value == '\r')
            {
                if (value == '\n') FlushLine();
            }
            else _buf.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_lock)
        {
            foreach (var ch in value)
            {
                if (ch == '\n') FlushLine();
                else if (ch == '\r') { /* 忽略 CR，以 LF 为行结束 */ }
                else _buf.Append(ch);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        lock (_lock) FlushLine();
    }

    public override void Write(char[] buffer, int index, int count)
        => Write(new string(buffer, index, count));

    private void FlushLine()
    {
        if (_buf.Length == 0) return;
        var line = _buf.ToString();
        _buf.Clear();
        // 释放锁后再写 IPC，避免与 IpcWriter 锁交叉
        _ipc.Emit(new { type = "console", text = line });
    }
}
