using DataMigrate.Core;
using DataMigrate.DataAccess;
using DataMigrate.Infrastructure;
using DataMigrate.Models;
using DataMigrate.Upload;
using DataMigrate.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace DataMigrate.Sidecar.Ipc;

/// <summary>
/// Sidecar 命令分发器：解析 stdin 请求，按命令路由到对应处理器。
/// 长任务（start / retryErrors）后台执行并异步回传 started/progress/result 事件；
/// 短任务同步返回 result。stop 取消当前运行。
/// </summary>
public sealed class SidecarHost : IDisposable
{
    private readonly IpcWriter _ipc;
    private readonly Logger _logger;
    private readonly string _logDir;

    private CancellationTokenSource? _runCts;
    private bool _running;

    public SidecarHost(IpcWriter ipc, Logger logger, string logDir)
    {
        _ipc = ipc;
        _logger = logger;
        _logDir = logDir;
    }

    public async Task HandleAsync(IpcRequest req)
    {
        try
        {
            switch (req.Cmd)
            {
                case "supportedTypes":
                    Respond(req, new SupportedTypes
                    {
                        Source = MigrationSourceFactory.SupportedTypes,
                        Dest = DataAccessorFactory.SupportedTypes
                    });
                    break;

                case "ping":
                    Respond(req, await PingAsync(req));
                    break;

                case "initDb":
                    Respond(req, await InitDbAsync(req));
                    break;

                case "getPlans":
                    Respond(req, new { plans = await GetPlansAsync(req) });
                    break;

                case "getErrors":
                    Respond(req, new { errors = await GetErrorsAsync(req) });
                    break;

                case "start":
                    RunBackground(req, "migration");
                    break;

                case "retryErrors":
                    RunRetryErrors(req);
                    break;

                case "stop":
                    StopRun(req);
                    break;

                default:
                    Respond(req, new { ok = false, message = $"未知命令: {req.Cmd}" });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "命令处理异常: {Cmd}", req.Cmd);
            _ipc.EmitError(req.Id, ex.Message);
        }
    }

    // ────── 短任务 ──────

    private async Task<object> PingAsync(IpcRequest req)
    {
        var side = req.GetArgString("side");
        var connStr = req.GetArgString("connStr");
        if (string.IsNullOrWhiteSpace(connStr))
            return new { ok = false, message = "connStr 为空" };

        if (side.Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            using var sp = BuildAdhocSp();
            try
            {
                var source = MigrationSourceFactory.Create(connStr, sp);
                await source.ValidateAsync();
                return new { ok = true, message = $"源数据库 {source.SourceName} 连接正常" };
            }
            catch (Exception ex)
            {
                return new { ok = false, message = $"源连接失败: {ex.Message}" };
            }
        }
        else
        {
            try
            {
                using var acc = DataAccessorFactory.Create(connStr);
                var ok = await acc.PingAsync();
                return new { ok, message = ok ? "目标数据库连接正常" : "目标数据库连接失败" };
            }
            catch (Exception ex)
            {
                return new { ok = false, message = $"目标连接失败: {ex.Message}" };
            }
        }
    }

    private async Task<object> InitDbAsync(IpcRequest req)
    {
        var profile = req.GetProfile();
        using var acc = DataAccessorFactory.Create(profile.ConnectionStrings.DestinationConn);
        using var sp = BuildAdhocSp(acc);
        var initializer = new DatabaseInitializer(acc, sp.GetRequiredService<ILogger<DatabaseInitializer>>());
        await initializer.EnsureAsync();
        return new { ok = true, message = "目标库初始化完成（幂等）" };
    }

    private async Task<List<PlanRow>> GetPlansAsync(IpcRequest req)
    {
        var connStr = req.GetArgString("connStr");
        var dbFlag = req.GetArgString("dbFlag");
        using var acc = DataAccessorFactory.Create(connStr);
        var rows = await acc.QueryAsync<PlanRow>(@"
            SELECT Id, DbFlag, StartTime, EndTime, TotalRecords, SuccessCount, FailedCount, Status, Msg
            FROM ZTemp_MigratePlan
            WHERE DbFlag = @DbFlag
            ORDER BY StartTime", new { DbFlag = dbFlag });
        return rows.ToList();
    }

    private async Task<List<ErrorRow>> GetErrorsAsync(IpcRequest req)
    {
        var connStr = req.GetArgString("connStr");
        var dbFlag = req.GetArgString("dbFlag");
        using var acc = DataAccessorFactory.Create(connStr);
        var rows = await acc.QueryAsync<ErrorRow>(@"
            SELECT Id, DbFlag, ImportTime, ErrorMessage
            FROM ZTemp_MigrateError
            WHERE DbFlag = @DbFlag
            ORDER BY ImportTime DESC", new { DbFlag = dbFlag });
        return rows.ToList();
    }

    private void StopRun(IpcRequest req)
    {
        if (_runCts is null)
        {
            _ipc.EmitError(req.Id, "无运行中的任务");
            return;
        }
        _runCts.Cancel();
        Respond(req, new { ok = true, message = "已发送停止信号" });
    }

    // ────── 后台长任务 ──────

    private void RunBackground(IpcRequest req, string label)
    {
        if (_running)
        {
            _ipc.EmitError(req.Id, "已有任务运行中，请先停止");
            return;
        }

        MigrationOptions profile;
        try { profile = req.GetProfile(); }
        catch (Exception ex)
        {
            _ipc.EmitError(req.Id, $"profile 解析失败: {ex.Message}");
            return;
        }

        var mode = (profile.Migration.Mode ?? "batch").ToLowerInvariant();
        _running = true;
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _ipc.Emit(new { id = req.Id, type = "started", label, mode });

        _ = Task.Run(async () =>
        {
            IServiceProvider? sp = null;
            try
            {
                sp = MigrationHost.BuildServiceProvider(profile, _logger, services =>
                {
                    services.AddSingleton(_ipc);
                    services.AddSingleton<IProgressReporter, IpcProgressReporter>();
                });
                var stats = sp.GetRequiredService<MigrationStatistics>();

                await DispatchModeAsync(sp, profile, mode, cts.Token);

                _ipc.Emit(new
                {
                    id = req.Id, type = "result", ok = true, label, mode,
                    summary = BuildSummary(stats),
                    total = stats.TotalRecords,
                    succeeded = stats.Succeeded,
                    failed = stats.Failed,
                    elapsedMs = (long)stats.Elapsed.TotalMilliseconds
                });
            }
            catch (OperationCanceledException)
            {
                var s = sp?.GetService<MigrationStatistics>();
                _ipc.Emit(new
                {
                    id = req.Id, type = "result", ok = false, label, mode, summary = "已取消",
                    total = s?.TotalRecords ?? 0, succeeded = s?.Succeeded ?? 0,
                    failed = s?.Failed ?? 0, elapsedMs = s != null ? (long)s.Elapsed.TotalMilliseconds : 0
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "运行异常: {Label}", label);
                _ipc.EmitError(req.Id, ex.Message);
            }
            finally
            {
                (sp as IDisposable)?.Dispose();
                if (ReferenceEquals(_runCts, cts)) _runCts = null;
                cts.Dispose();
                _running = false;
            }
        });
    }

    private void RunRetryErrors(IpcRequest req)
    {
        if (_running)
        {
            _ipc.EmitError(req.Id, "已有任务运行中，请先停止");
            return;
        }

        MigrationOptions profile;
        var ids = req.GetArgStringArray("ids") ?? Array.Empty<string>();
        try { profile = req.GetProfile(); }
        catch (Exception ex)
        {
            _ipc.EmitError(req.Id, $"profile 解析失败: {ex.Message}");
            return;
        }
        if (ids.Length == 0)
        {
            _ipc.EmitError(req.Id, "未指定要重试的记录 ids");
            return;
        }

        _running = true;
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _ipc.Emit(new { id = req.Id, type = "started", label = "retryErrors", mode = "retry", count = ids.Length });

        _ = Task.Run(async () =>
        {
            IServiceProvider? sp = null;
            try
            {
                sp = MigrationHost.BuildServiceProvider(profile, _logger, services =>
                {
                    services.AddSingleton(_ipc);
                    services.AddSingleton<IProgressReporter, IpcProgressReporter>();
                });
                var stats = sp.GetRequiredService<MigrationStatistics>();

                await RetryIdsAsync(sp, profile, ids, cts.Token);

                _ipc.Emit(new
                {
                    id = req.Id, type = "result", ok = true, label = "retryErrors", mode = "retry",
                    summary = BuildSummary(stats),
                    total = stats.TotalRecords, succeeded = stats.Succeeded,
                    failed = stats.Failed, elapsedMs = (long)stats.Elapsed.TotalMilliseconds
                });
            }
            catch (OperationCanceledException)
            {
                var s = sp?.GetService<MigrationStatistics>();
                _ipc.Emit(new
                {
                    id = req.Id, type = "result", ok = false, label = "retryErrors", summary = "已取消",
                    total = s?.TotalRecords ?? 0, succeeded = s?.Succeeded ?? 0,
                    failed = s?.Failed ?? 0, elapsedMs = s != null ? (long)s.Elapsed.TotalMilliseconds : 0
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "重试异常");
                _ipc.EmitError(req.Id, ex.Message);
            }
            finally
            {
                (sp as IDisposable)?.Dispose();
                if (ReferenceEquals(_runCts, cts)) _runCts = null;
                cts.Dispose();
                _running = false;
            }
        });
    }

    /// <summary>按 profile.Migration.Mode 分派执行。</summary>
    private async Task DispatchModeAsync(IServiceProvider sp, MigrationOptions profile, string mode, CancellationToken ct)
    {
        switch (mode)
        {
            case "debug":
                await RunDebugAsync(sp, profile, ct);
                break;
            case "retry":
                await RunRetryAllAsync(sp, profile, ct);
                break;
            default:
                // batch：先校验+建表，再执行
                await sp.GetRequiredService<ConfigValidator>().ValidateAllAsync();
                await sp.GetRequiredService<MigrationRunner>().RunAsync(ct);
                break;
        }
    }

    private async Task RunDebugAsync(IServiceProvider sp, MigrationOptions profile, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<SidecarHost>>();
        var source = sp.GetRequiredService<IMigrationSource>();
        var uploader = sp.GetRequiredService<ExamUploader>();
        var stats = sp.GetRequiredService<MigrationStatistics>();

        var examId = profile.Migration.ExamId ?? "";
        var parts = examId.Split('|');
        if (parts.Length != 2)
            throw new InvalidOperationException("ExamId 格式错误，应为 OrgCode|ExamId");

        stats.TotalRecords = 1;
        stats.Start();

        logger.LogInformation("调试模式: 单条 {ExamId}", examId);
        var archive = await source.GetByExamIdAsync(parts[0], parts[1]);
        if (archive == null)
        {
            logger.LogWarning("未找到记录: {ExamId}", examId);
            stats.IncrementFailed();
        }
        else
        {
            // 回传单条数据供前端查看
            _ipc.Emit(new { type = "debug", archive });
            var result = await uploader.UploadAsync(archive, ct);
            if (result.Success) stats.IncrementSucceeded(); else stats.IncrementFailed();
        }

        await uploader.FlushRemainingAsync();
        stats.Stop();
    }

    private async Task RunRetryAllAsync(IServiceProvider sp, MigrationOptions profile, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<SidecarHost>>();
        var accessor = sp.GetRequiredService<IDataAccessor>();
        var source = sp.GetRequiredService<IMigrationSource>();
        var uploader = sp.GetRequiredService<ExamUploader>();
        var stats = sp.GetRequiredService<MigrationStatistics>();
        var dbFlag = profile.Migration.DbFlag;

        logger.LogInformation("错误重试模式: 从 ZTemp_MigrateError 读取失败记录...");
        var errorIds = (await accessor.QueryAsync<string>(
            "SELECT Id FROM ZTemp_MigrateError WHERE DbFlag=@DbFlag",
            new { DbFlag = dbFlag })).ToList();

        stats.TotalRecords = errorIds.Count;
        stats.Start();

        foreach (var errorId in errorIds)
        {
            ct.ThrowIfCancellationRequested();
            var parts = errorId.Split('|');
            if (parts.Length != 2) continue;

            var archive = await source.GetByExamIdAsync(parts[0], parts[1]);
            if (archive == null) { stats.IncrementFailed(); continue; }

            var result = await uploader.UploadAsync(archive, ct);
            if (result.Success) stats.IncrementSucceeded(); else stats.IncrementFailed();
        }

        await uploader.FlushRemainingAsync();
        stats.Stop();
    }

    private async Task RetryIdsAsync(IServiceProvider sp, MigrationOptions profile, string[] ids, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<SidecarHost>>();
        var source = sp.GetRequiredService<IMigrationSource>();
        var uploader = sp.GetRequiredService<ExamUploader>();
        var stats = sp.GetRequiredService<MigrationStatistics>();

        stats.TotalRecords = ids.Length;
        stats.Start();

        foreach (var errorId in ids)
        {
            ct.ThrowIfCancellationRequested();
            var parts = errorId.Split('|');
            if (parts.Length != 2) { stats.IncrementFailed(); continue; }

            var archive = await source.GetByExamIdAsync(parts[0], parts[1]);
            if (archive == null) { stats.IncrementFailed(); continue; }

            var result = await uploader.UploadAsync(archive, ct);
            if (result.Success) stats.IncrementSucceeded(); else stats.IncrementFailed();
        }

        await uploader.FlushRemainingAsync();
        stats.Stop();
    }

    // ────── 工具 ──────

    private void Respond(IpcRequest req, object data) =>
        _ipc.Emit(new { id = req.Id, type = "result", ok = true, data });

    private static string BuildSummary(MigrationStatistics stats) =>
        $"总数 {stats.TotalRecords:N0}，成功 {stats.Succeeded:N0}，失败 {stats.Failed:N0}，"
        + $"耗时 {stats.Elapsed:hh\\:mm\\:ss}，速度 {stats.RecordsPerSecond:F1}/s";

    /// <summary>构建仅含日志的临时 ServiceProvider（用于 ping/initDb 的 ILogger 解析）。</summary>
    private ServiceProvider BuildAdhocSp(IDataAccessor? accessor = null)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => { b.ClearProviders(); b.AddSerilog(_logger, dispose: false); });
        if (accessor != null) sc.AddSingleton(accessor);
        return sc.BuildServiceProvider();
    }

    public void Dispose()
    {
        try { _runCts?.Cancel(); } catch { }
        _runCts?.Dispose();
    }
}
