using DataMigrate.DataAccess;
using DataMigrate.Infrastructure;
using DataMigrate.Models;
using DataMigrate.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Core;

/// <summary>
/// 迁移编排器：将时间范围切割为多个子计划，并发执行并汇总统计。
/// 计划数据管理委托给 PlanManager，本类只负责调度与运行时控制。
/// </summary>
public class MigrationRunner
{
    private readonly IMigrationSource _source;
    private readonly MigrationOptions _options;
    private readonly IServiceProvider _sp;
    private readonly MigrationStatistics _stats;
    private readonly IProgressReporter _progress;
    private readonly ILogger<MigrationRunner> _logger;
    private readonly PlanManager _planManager;

    public MigrationRunner(
        IMigrationSource source,
        MigrationOptions options,
        IServiceProvider sp,
        MigrationStatistics statistics,
        IProgressReporter progress,
        ILogger<MigrationRunner> logger,
        PlanManager planManager)
    {
        _source = source;
        _options = options;
        _sp = sp;
        _stats = statistics;
        _progress = progress;
        _logger = logger;
        _planManager = planManager;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var cfg = _options.Migration.TimeRange;
        if (cfg.Start == default || cfg.End == default)
        {
            _logger.LogWarning("batch 模式需配置 Migration.TimeRange");
            return;
        }

        var fullRange = new DateRange(cfg.Start, cfg.End);
        _logger.LogInformation("批量迁移: {Start:yyyy-MM-dd HH:mm} ~ {End:yyyy-MM-dd HH:mm}",
            fullRange.Start, fullRange.End);

        await RunPlanBasedMigrationAsync(fullRange, ct);
    }

    private async Task RunPlanBasedMigrationAsync(DateRange fullRange, CancellationToken ct)
    {
        var plans = PlanManager.SplitRange(fullRange, _options.Migration.PlanIntervalDays);
        _logger.LogInformation("计划切割: 共 {Count} 个子计划, 间隔 {IntervalDays} 天",
            plans.Count, _options.Migration.PlanIntervalDays);

        // 1. 查询已有计划（支持中断后恢复）
        var existing = await _planManager.QueryExistingAsync();

        // 兼容旧表：旧表 StartTime/EndTime 存的是执行时间而非计划时间范围，
        // 此处直接复用计划切割逻辑，已有记录若 Status=Completed 则跳过

        // 2. 合并：插入新计划，返回待执行列表（含复用 Id）
        var toExecute = await _planManager.MergeAsync(plans, existing);

        if (toExecute.Count == 0)
        {
            _logger.LogInformation("所有计划均已完成，无需执行");
            _stats.TotalRecords = 0;
            return;
        }

        _logger.LogInformation("待执行计划: {Count}/{Total}（跳过 {Skipped} 个已完成）",
            toExecute.Count, plans.Count, plans.Count - toExecute.Count);

        // 3. 设置全局统计总数
        var totalMeta = await _source.GetMetadataAsync(fullRange);
        _stats.TotalRecords = totalMeta.EstimatedCount;
        _logger.LogInformation("全局预估记录数: {Count}", _stats.TotalRecords);

        // 4. 按配置排序
        List<PlanInfo> sortedPlans = _options.Migration.PlanOrder.Equals("descending", StringComparison.OrdinalIgnoreCase)
            ? [.. toExecute.OrderBy(p => p.Range.Start)]
            : [.. toExecute.OrderByDescending(p => p.Range.Start)];

        // 5. 启动全局进度与计时
        _stats.Start();
        _progress.Start();

        // 6. 并发执行子计划
        await Parallel.ForEachAsync(sortedPlans, new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.Migration.MaxParallelPlans,
            CancellationToken = ct
        }, async (plan, innerCt) =>
        {
            var engine = ActivatorUtilities.CreateInstance<MigrationEngine>(_sp, plan.Id);
            _logger.LogInformation("启动计划: {PlanId}, 范围 {Start:yyyy-MM-dd HH:mm} ~ {End:yyyy-MM-dd HH:mm}",
                plan.Id, plan.Range.Start, plan.Range.End);
            await engine.RunAsync(plan.Range, innerCt);
        });

        // 7. 停止全局进度
        _progress.Stop();
        _stats.Stop();

        // 8. 汇总
        _stats.PrintSummary();
    }
}
