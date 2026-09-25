using DataMigrate.DataAccess;
using DataMigrate.Models;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Core;

/// <summary>
/// 迁移计划数据管理：切割、持久化、合并已有计划。
/// 与 MigrationRunner 的边界：本类只负责 ZTemp_MigratePlan 表的数据操作，
/// 不参与执行调度、统计、进度等运行时逻辑。
/// </summary>
public class PlanManager
{
    private readonly IDataAccessor _accessor;
    private readonly string _dbFlag;
    private readonly ILogger<PlanManager> _logger;

    public PlanManager(IDataAccessor accessor, MigrationOptions options, ILogger<PlanManager> logger)
    {
        _accessor = accessor;
        _dbFlag = options.Migration.DbFlag;
        _logger = logger;
    }

    /// <summary>将完整时间范围按 intervalDays 切割为子计划列表</summary>
    public static List<PlanInfo> SplitRange(DateRange full, int intervalDays)
    {
        var plans = new List<PlanInfo>();
        var cur = full.Start;
        var lastEnd = full.End.AddSeconds(1);

        while (cur < full.End)
        {
            var candidateEnd = cur.AddDays(intervalDays);
            var actualEnd = candidateEnd >= full.End ? lastEnd : candidateEnd;
            plans.Add(new PlanInfo(Guid.NewGuid(), new DateRange(cur, actualEnd)));
            cur = actualEnd;
        }

        return plans;
    }

    /// <summary>
    /// 查询已有计划，按 StartTime（即计划的起止时间）去重，返回字典供合并使用。
    /// Status=Completed 表示已完成，需跳过；其他状态重新执行。
    /// </summary>
    public async Task<Dictionary<DateTime, (Guid Id, string Status)>> QueryExistingAsync()
    {
        var rows = await _accessor.QueryAsync<MigratePlanRecord>(@"
            SELECT Id, StartTime, EndTime, TotalRecords, SuccessCount, FailedCount, Status
            FROM ZTemp_MigratePlan
            WHERE DbFlag = @DbFlag",
            new { DbFlag = _dbFlag });

        var result = new Dictionary<DateTime, (Guid Id, string Status)>();
        foreach (var r in rows)
        {
            // StartTime 即计划时间范围的起点，用于断点续跑匹配
            if (!result.ContainsKey(r.StartTime))
                result[r.StartTime] = (r.Id, string.IsNullOrWhiteSpace(r.Status)
                    ? MigratePlanStatus.Pending.ToString()
                    : r.Status);
        }
        _logger.LogInformation("查询到 {Count} 条已有计划记录, DbFlag={DbFlag}", result.Count, _dbFlag);
        return result;
    }

    /// <summary>
    /// 合并计划：已完成（Status=Completed）的跳过，未完成或未写入的复用 Id，不存在的插入新记录。
    /// 返回需要执行的计划列表（含正确的 Id）。
    /// </summary>
    public async Task<List<PlanInfo>> MergeAsync(List<PlanInfo> planned, Dictionary<DateTime, (Guid Id, string Status)> existing)
    {
        var toExecute = new List<PlanInfo>();
        var toInsert = new List<PlanInfo>();

        foreach (var plan in planned)
        {
            if (existing.TryGetValue(plan.Range.Start, out var existingPlan))
            {
                if (string.Equals(existingPlan.Status, MigratePlanStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("计划已完成，跳过: {Id} {Start:yyyy-MM-dd} ~ {End:yyyy-MM-dd}",
                        existingPlan.Id, plan.Range.Start.Date, plan.Range.End.Date);
                    continue;
                }
                toExecute.Add(new PlanInfo(existingPlan.Id, plan.Range));
                _logger.LogDebug("计划需重新执行: {Id}, {Start:yyyy-MM-dd} ~ {End:yyyy-MM-dd}",
                    existingPlan.Id, plan.Range.Start.Date, plan.Range.End.Date);
            }
            else
            {
                toInsert.Add(plan);
                toExecute.Add(plan);
            }
        }

        if (toInsert.Count > 0)
            await InsertAsync(toInsert);

        return toExecute;
    }

    private async Task InsertAsync(List<PlanInfo> plans)
    {
        try
        {
            var tx = await _accessor.BeginTransactionAsync();
            try
            {
                foreach (var plan in plans)
                {
                    await _accessor.ExecuteAsync(@"
INSERT INTO ZTemp_MigratePlan (Id, DbFlag, StartTime, EndTime, Status, Msg)
VALUES (@Id, @DbFlag, @Start, @End, 'Pending', NULL)",
                        new {
                            Id = plan.Id,
                            DbFlag = _dbFlag,
                            Start = plan.Range.Start,
                            End = plan.Range.End
                        }, transaction: tx);
                }
                tx.Commit();
                _logger.LogInformation("已写入 {Count} 条计划记录, DbFlag={DbFlag}", plans.Count, _dbFlag);
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量写入计划表失败");
            throw;
        }
    }
}
