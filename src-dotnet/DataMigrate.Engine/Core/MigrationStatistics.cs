using System.Diagnostics;

namespace DataMigrate.Core;

/// <summary>线程安全的迁移统计计数器</summary>
public class MigrationStatistics
{
    private long _succeeded;
    private long _failed;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();

    public long TotalRecords { get; set; }
    public long Processed => Succeeded + Failed;
    public long Succeeded { get { lock (_lock) return _succeeded; } }
    public long Failed { get { lock (_lock) return _failed; } }
    public double RecordsPerSecond => _stopwatch.Elapsed.TotalSeconds > 0
        ? Processed / _stopwatch.Elapsed.TotalSeconds
        : 0;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Start() => _stopwatch.Start();
    public void Stop() => _stopwatch.Stop();

    public void IncrementSucceeded()
    {
        lock (_lock) Interlocked.Increment(ref _succeeded);
    }

    public void IncrementFailed()
    {
        lock (_lock) Interlocked.Increment(ref _failed);
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("  ═══════════════════════════════════════");
        Console.WriteLine($"  总记录:     {TotalRecords,10:N0}");
        Console.WriteLine($"  成功:       {Succeeded,10:N0}");
        Console.WriteLine($"  失败:       {Failed,10:N0}");
        Console.WriteLine($"  耗时:       {Elapsed,10:hh\\:mm\\:ss}");
        Console.WriteLine($"  速度:       {RecordsPerSecond,10:F1} 条/秒");
        Console.WriteLine("  ═══════════════════════════════════════");
    }
}
