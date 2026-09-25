using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using DataMigrate.DataAccess;
using DataMigrate.Models;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Upload;

/// <summary>
/// 上传结果在 ZTemp 缓冲中的处理方式。
/// </summary>
public enum ExamTrackMode
{
    /// <summary>仅缓冲失败记录（batch 模式）</summary>
    BufferFailures,

    /// <summary>缓冲成功+失败（retry 模式：成功记录用于 flush 时 DELETE 原失败记录）</summary>
    BufferAll,

    /// <summary>直接写 DB，不缓冲（debug 模式：单条调试）</summary>
    Direct,
}

/// <summary>
/// HTTP 上传 + ZTemp 错误缓冲。
/// 
/// 缓冲区策略（按 ExamTrackMode 区分）：
///   - BufferFailures (batch)：仅缓冲失败记录（成功无需跟踪）
///   - BufferAll (retry)：缓冲全部结果（成功记录用于 flush 时 DELETE 原失败记录）
///   - Direct (debug)：直接写 DB，不缓冲（单条调试）
/// 
/// Flush 策略（先删后插，幂等）：
///   1. DELETE 本批次所有 Id 的旧记录（清理历史，防重复）
///   2. 批量 INSERT 失败记录（一条 SQL，chunk 化避免参数上限）
/// </summary>
public class ExamUploader
{
    private readonly HttpClient _http;
    private readonly JwtTokenManager _jwt;
    private readonly IDataAccessor _accessor;
    private readonly string _dbFlag;
    private readonly int _batchSize;
    private readonly ExamTrackMode _trackMode;
    private readonly ILogger<ExamUploader> _logger;

    private readonly List<(string Id, bool Success, string? Error)> _buffer = new();
    private readonly object _bufferLock = new();

    // SQL Server 参数上限 2100，DELETE 每行 1 参数 / INSERT 每行 5 参数，留足余量
    private const int DeleteChunkSize = 800;
    private const int InsertChunkSize = 400;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ExamUploader(
        HttpClient http,
        JwtTokenManager jwt,
        IDataAccessor accessor,
        string dbFlag,
        int batchSize,
        ILogger<ExamUploader> logger,
        ExamTrackMode trackMode = ExamTrackMode.BufferFailures)
    {
        _http = http;
        _jwt = jwt;
        _accessor = accessor;
        _dbFlag = dbFlag;
        _batchSize = batchSize;
        _trackMode = trackMode;
        _logger = logger;
    }

    public async Task<ExamUploadResult> UploadAsync(ExamUploadReq archive, CancellationToken ct)
    {
        var id = $"{archive.OrgCode}|{archive.ExamId}";

        ExamUploadResult result;
        try
        {
            var token = _jwt.GetToken();
            //var hasReportUrl = !string.IsNullOrWhiteSpace(archive.Report?.ReportUrl);

            //if (hasReportUrl)
            //{
            //    result = await UploadWithPdfAsync(archive, id, token, ct);
            //}
            //else
            //{
                result = await UploadJsonAsync(archive, id, token, ct);
            //}
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result = new ExamUploadResult { Success = false, ErrorMessage = ex.Message };
        }

        // 按运行模式选择记录策略：
        //   - Direct       (debug): 失败直接写 DB，成功不记录
        //   - BufferAll    (retry): 成功和失败都入 buffer，flush 时统一处理
        //   - BufferFailures (batch): 仅失败入 buffer
        switch (_trackMode)
        {
            case ExamTrackMode.Direct:
                if (!result.Success)
                    await DirectRecordFailureAsync(id, result.ErrorMessage);
                break;
            case ExamTrackMode.BufferAll:
                await RecordResultAsync(id, result.Success, result.ErrorMessage);
                break;
            default: // BufferFailures
                if (!result.Success)
                    await RecordResultAsync(id, result.Success, result.ErrorMessage);
                break;
        }

        return result;
    }

    /// <summary>debug 模式专用：失败直接 INSERT，不走 buffer。</summary>
    private async Task DirectRecordFailureAsync(string id, string? error)
    {
        try
        {
            await _accessor.ExecuteAsync(
                "INSERT INTO ZTemp_MigrateError (Id, DbFlag, ImportTime, ErrorMessage) " +
                "VALUES (@Id, @DbFlag, @ImportTime, @ErrorMessage)",
                new { Id = id, DbFlag = _dbFlag, ImportTime = DateTime.Now, ErrorMessage = error });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direct 写入 ZTemp_MigrateError 失败: {Id}", id);
        }
    }

    private async Task<ExamUploadResult> UploadJsonAsync(ExamUploadReq archive, string id, string token, CancellationToken ct)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(archive, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = content
        };
        request.Headers.Add("token", $"Bearer {token}");

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return new ExamUploadResult { Success = true };

        var body = await response.Content.ReadAsStringAsync(ct);
        return new ExamUploadResult { Success = false, ErrorMessage = $"HTTP {(int)response.StatusCode}: {body}" };
    }

    private async Task<ExamUploadResult> UploadWithPdfAsync(ExamUploadReq archive, string id, string token, CancellationToken ct)
    {
        try
        {
            // 下载 PDF
            using var httpClient = new HttpClient();
            var pdfBytes = await httpClient.GetByteArrayAsync(archive.Report!.ReportUrl, ct);
            var pdfBase64 = Convert.ToBase64String(pdfBytes);

            var uploadReq = new ExamUploadWithPdfReq
            {
                Data = archive,
                PdfReport = pdfBase64
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(uploadReq, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "/UploadWithPdf")
            {
                Content = content
            };
            request.Headers.Add("token", $"Bearer {token}");

            var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new ExamUploadResult { Success = true };

            var body = await response.Content.ReadAsStringAsync(ct);
            return new ExamUploadResult { Success = false, ErrorMessage = $"PDF上传失败: HTTP {(int)response.StatusCode}: {body}" };
        }
        catch (Exception ex)
        {
            return new ExamUploadResult { Success = false, ErrorMessage = $"PDF上传失败: {ex.Message}" };
        }
    }

    private async Task RecordResultAsync(string id, bool success, string? error)
    {
        lock (_bufferLock) { _buffer.Add((id, success, error)); }
        bool shouldFlush;
        lock (_bufferLock) { shouldFlush = _buffer.Count >= _batchSize; }
        if (shouldFlush)
            await FlushAsync();
    }

    public async Task FlushAsync()
    {
        List<(string Id, bool Success, string? Error)> batch;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;
            batch = new List<(string, bool, string?)>(_buffer);
            _buffer.Clear();
        }

        IDbTransaction? tx = null;
        try
        {
            tx = await _accessor.BeginTransactionAsync();

            // 1. 先删除本批次所有 Id 对应的旧记录（清理历史，防重复）
            var allIds = batch.Select(b => b.Id).Distinct().ToList();
            await DeleteBatchAsync(allIds, tx);

            // 2. 批量插入失败记录
            var failures = batch.Where(b => !b.Success).ToList();
            if (failures.Count > 0)
            {
                await InsertFailuresAsync(failures, tx);
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZTemp 批量写入失败, 回滚 {Count} 条到缓冲区", batch.Count);
            try { tx?.Rollback(); } catch { }
            lock (_bufferLock) { _buffer.InsertRange(0, batch); }
        }
        finally
        {
            tx?.Dispose();
        }
    }

    private async Task DeleteBatchAsync(List<string> ids, IDbTransaction tx)
    {
        if (ids.Count == 0) return;

        for (int offset = 0; offset < ids.Count; offset += DeleteChunkSize)
        {
            var chunk = ids.GetRange(offset, Math.Min(DeleteChunkSize, ids.Count - offset));
            var dp = new DynamicParameters();
            dp.Add("DbFlag", _dbFlag);
            for (int i = 0; i < chunk.Count; i++)
                dp.Add($"id{i}", chunk[i]);

            var inClause = string.Join(",",
                Enumerable.Range(0, chunk.Count).Select(i => $"@id{i}"));

            await _accessor.ExecuteAsync(
                $"DELETE FROM ZTemp_MigrateError WHERE DbFlag=@DbFlag AND Id IN ({inClause})",
                dp, transaction: tx);
        }
    }

    private async Task InsertFailuresAsync(
        List<(string Id, bool Success, string? Error)> failures, IDbTransaction tx)
    {
        var now = DateTime.Now;

        for (int offset = 0; offset < failures.Count; offset += InsertChunkSize)
        {
            var chunk = failures.GetRange(offset, Math.Min(InsertChunkSize, failures.Count - offset));
            var dp = new DynamicParameters();
            var valuesBuilder = new StringBuilder(chunk.Count * 60);

            for (int i = 0; i < chunk.Count; i++)
            {
                if (i > 0) valuesBuilder.Append(",");
                valuesBuilder.Append($"(@id{i},@flag{i},@time{i},@msg{i})");

                dp.Add($"id{i}", chunk[i].Id);
                dp.Add($"flag{i}", _dbFlag);
                dp.Add($"time{i}", now);
                dp.Add($"msg{i}", chunk[i].Error);
            }

            // ArchiveType 使用表默认值 'ExamApproval'，跨库兼容
            await _accessor.ExecuteAsync(
                "INSERT INTO ZTemp_MigrateError (Id, DbFlag, ImportTime, ErrorMessage) VALUES " +
                valuesBuilder.ToString(),
                dp, transaction: tx);
        }
    }

    public async Task FlushRemainingAsync() => await FlushAsync();
}

public class ExamUploadResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
