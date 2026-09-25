namespace DataMigrate.Core;

/// <summary>
/// 进度上报抽象。控制台实现为 ConsoleProgressBar（绘制进度条），
/// 桌面 Sidecar 实现为 IpcProgressReporter（通过 IPC 发送 progress 事件）。
/// MigrationRunner 仅依赖此接口，解耦运行时与具体展示。
/// </summary>
public interface IProgressReporter
{
    /// <summary>开始周期性上报</summary>
    void Start();
    /// <summary>停止上报</summary>
    void Stop();
}
