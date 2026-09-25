using DataMigrate.Core;

namespace DataMigrate.Infrastructure;

/// <summary>控制台实时进度条：Timer 驱动，500ms 延迟后每秒刷新一次</summary>
public class ConsoleProgressBar : IProgressReporter, IDisposable
{
    private readonly MigrationStatistics _stats;
    private Timer? _timer;
    private bool _disposed;

    public ConsoleProgressBar(MigrationStatistics stats)
    {
        _stats = stats;
    }

    public void Start()
    {
        if (_timer != null) return;
        _timer = new Timer(_ => Render(), null, 500, 1000);
    }

    public void Stop()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        var line = new string(' ', Math.Max(0, Console.WindowWidth - 1));
        Console.CursorLeft = 0;
        Console.Write(line);
        Console.CursorLeft = 0;
    }

    private void Render()
    {
        if (_disposed || _stats.TotalRecords == 0) return;
        var pct = Math.Min(1.0, (double)_stats.Processed / _stats.TotalRecords);
        var cw = Math.Max(40, Console.WindowWidth - 5);
        var barWidth = Math.Max(1, cw - 45);
        var filled = (int)(pct * barWidth);
        var bar = new string('█', filled) + new string('░', barWidth - filled);

        try
        {
            Console.CursorLeft = 0;
            Console.Write($"{bar} {pct,6:P1}  {_stats.Processed,7:N0}/{_stats.TotalRecords,7:N0}  {_stats.RecordsPerSecond,5:F1}/s");
        }
        catch { }
    }

    public void Dispose() => Stop();
}
