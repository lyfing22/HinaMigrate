using System.Threading.Channels;
using DataMigrate.DataAccess;
using DataMigrate.Models;
using DataMigrate.Upload;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Core;

/// <summary>
/// Producer-Consumer 迁移引擎（单个计划执行单元）：
/// 1 个生产者分页查源库 → Channel&lt;ExamUploadReq&gt; 有界队列 → N 个消费者并行 HTTP 上传
/// 不再管理全局统计/进度/审计，这些由 MigrationRunner 统一控制。
/// </summary>
public class MigrationEngine
{
    private readonly IMigrationSource _source;
    private readonly ExamUploader _uploader;
    private readonly MigrationStatistics _stats;
    private readonly MigrationOptions _options;
    private readonly ILogger<MigrationEngine> _logger;
    private readonly IDataAccessor _accessor;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<ExamUploadReq> _channel;
    private readonly int _parallelism;
    private readonly Guid _planId;

    private int _succeeded;
    private int _failed;
    private int _processed;

    public MigrationEngine(
        IMigrationSource source,
        ExamUploader uploader,
        MigrationStatistics statistics,
        MigrationOptions options,
        ILogger<MigrationEngine> logger,
        IDataAccessor accessor,
        Guid planId)
    {
        _source = source;
        _uploader = uploader;
        _stats = statistics;
        _options = options;
        _logger = logger;
        _accessor = accessor;
        _planId = planId;
        _parallelism = options.Migration.Parallelism > 0 ? options.Migration.Parallelism : 4;
        _channel = Channel.CreateBounded<ExamUploadReq>(new BoundedChannelOptions(options.Migration.PageSize * 2)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>执行单个计划：Produce → Consume → 刷缓冲 → 更新计划状态</summary>
    public async Task RunAsync(DateRange range, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var token = linkedCts.Token;

        _logger.LogInformation("计划执行: PlanId={PlanId}, Range={Start:yyyy-MM-dd HH:mm} ~ {End:yyyy-MM-dd HH:mm}",
            _planId, range.Start, range.End);

        try
        {
            var meta = await _source.GetMetadataAsync(range);
            if (meta.EstimatedCount == 0)
            {
                _logger.LogInformation("计划范围内无数据");
                await UpdatePlanResultAsync(MigratePlanStatus.Completed, "无数据", 0, 0);
                return;
            }

            await UpdatePlanRunningAsync(meta.EstimatedCount);

            var producer = ProduceAsync(range, token);
            var consumers = new Task[_parallelism];
            for (int i = 0; i < _parallelism; i++)
            {
                var id = i;
                consumers[i] = ConsumeAsync(id, token);
            }

            await producer;
            _channel.Writer.TryComplete();
            await Task.WhenAll(consumers);

            await _uploader.FlushRemainingAsync();
            var status = _failed == 0 ? MigratePlanStatus.Completed : MigratePlanStatus.Failed;
            var msg = _failed > 0 ? $"失败 {_failed} 条，详见 ZTemp_MigrateError" : "全部成功";
            await UpdatePlanResultAsync(status, msg, _succeeded, _failed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("计划被取消: {PlanId}", _planId);
            _channel.Writer.TryComplete();
            await _uploader.FlushRemainingAsync();
            await UpdatePlanResultAsync(MigratePlanStatus.Failed, "用户取消", _succeeded, _failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计划异常终止: {PlanId}", _planId);
            _channel.Writer.TryComplete();
            await _uploader.FlushRemainingAsync();
            await UpdatePlanResultAsync(MigratePlanStatus.Failed, TruncateMsg(ex.Message), _succeeded, _failed);
            throw;
        }
    }

    private async Task ProduceAsync(DateRange range, CancellationToken ct)
    {
        await foreach (var archive in _source.EnumerateArchivesAsync(range, _options.Migration.PageSize, ct))
        {
            await _channel.Writer.WriteAsync(archive, ct);
        }
    }

    private async Task ConsumeAsync(int id, CancellationToken ct)
    {
        await foreach (var archive in _channel.Reader.ReadAllAsync(ct))
        {
            var result = await _uploader.UploadAsync(archive, ct);
            if (result.Success)
            {
                _stats.IncrementSucceeded();
                _succeeded++;
                _logger.LogDebug("Consumer#{Id} 成功: {OrgCode}|{ExamId}", id, archive.OrgCode, archive.ExamId);
            }
            else
            {
                _stats.IncrementFailed();
                _failed++;
                _logger.LogWarning("Consumer#{Id} 失败: {OrgCode}|{ExamId} => {Msg}",
                    id, archive.OrgCode, archive.ExamId, result.ErrorMessage);
            }

            _processed++;
            if (_processed % (_options.Migration.ZTempBatchSize * 2) == 0)
                await UpdatePlanProgressAsync();
        }
    }

    // ────── ZTemp_MigratePlan 追踪表操作 ──────

    private async Task UpdatePlanRunningAsync(int totalRecords)
    {
        try
        {
            await _accessor.ExecuteAsync(@"
UPDATE ZTemp_MigratePlan
SET Status = 'Running', TotalRecords = @TotalRecords
WHERE Id = @Id",
                new { Id = _planId, TotalRecords = totalRecords });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新计划表状态失败");
        }
    }

    private async Task UpdatePlanResultAsync(MigratePlanStatus status, string? msg, int successCount, int failedCount)
    {
        try
        {
            await _accessor.ExecuteAsync(@"
UPDATE ZTemp_MigratePlan
SET Status = @Status,
    SuccessCount = @SuccessCount,
    FailedCount = @FailedCount,
    Msg = @Msg
WHERE Id = @Id",
                new
                {
                    Id = _planId,
                    Status = status.ToString(),
                    SuccessCount = successCount,
                    FailedCount = failedCount,
                    Msg = TruncateMsg(msg)
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新计划表状态失败");
        }
    }

    private static string? TruncateMsg(string? msg)
    {
        if (string.IsNullOrEmpty(msg))
            return msg;
        return msg.Length <= 512 ? msg : msg[..512];
    }

    private async Task UpdatePlanProgressAsync()
    {
        try
        {
            await _accessor.ExecuteAsync(@"
UPDATE ZTemp_MigratePlan
SET SuccessCount = @Ok, FailedCount = @Fail
WHERE Id = @Id",
                new { Id = _planId, Ok = _succeeded, Fail = _failed });
        }
        catch { }
    }
}
