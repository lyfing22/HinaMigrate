using DataMigrate.Core;
using DataMigrate.Infrastructure;

namespace DataMigrate.Sidecar.Ipc;

/// <summary>
/// 桌面端进度上报器：Timer 周期读取 MigrationStatistics 快照，经 IpcWriter
/// 发送 progress 事件。实现 <see cref="IProgressReporter"/>，由 MigrationRunner 通过 DI 注入。
/// </summary>
public sealed class IpcProgressReporter : IProgressReporter, IDisposable
{
    private readonly MigrationStatistics _stats;
    private readonly IpcWriter _ipc;
    private Timer? _timer;
    private bool _disposed;

    public IpcProgressReporter(MigrationStatistics stats, IpcWriter ipc)
    {
        _stats = stats;
        _ipc = ipc;
    }

    public void Start()
    {
        if (_timer != null) return;
        // 500ms 延迟后每 500ms 上报一次（前端可进一步节流）
        _timer = new Timer(_ => Emit(), null, 500, 500);
    }

    public void Stop()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _timer = null;
        // 停止时再发一帧最终进度
        Emit();
    }

    private void Emit()
    {
        if (_stats.TotalRecords == 0 && _stats.Processed == 0) return;
        _ipc.Emit(new
        {
            type = "progress",
            total = _stats.TotalRecords,
            processed = _stats.Processed,
            succeeded = _stats.Succeeded,
            failed = _stats.Failed,
            elapsedMs = (long)_stats.Elapsed.TotalMilliseconds,
            rps = Math.Round(_stats.RecordsPerSecond, 1)
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
