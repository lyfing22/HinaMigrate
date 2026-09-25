using System.Runtime.CompilerServices;
using DataMigrate.Core;
using DataMigrate.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Sources.SqlServer;

/// <summary>
/// SQL Server 数据源实现（骨架）
///
/// 表结构 / 列映射 / 主键约定尚未确认，当前仅实现连通性探测；
/// 其余方法留空并抛出明确异常，待源表结构确定后补齐。
/// </summary>
public class SqlServerSource : IMigrationSource
{
    public string SourceName => "SqlServer";

    private readonly string _connectionString;
    private readonly ILogger<SqlServerSource> _logger;

    public SqlServerSource(string connectionString, ILogger<SqlServerSource> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ValidateAsync()
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SQL Server 连接失败: {ex.Message}", ex);
        }
    }

    public Task<SourceMetadata> GetMetadataAsync(DateRange range)
    {
        // TODO: 待源表结构确认后实现（COUNT + MIN/CreateDateTime + MAX/CreateDateTime）
        _logger.LogWarning("SqlServerSource.GetMetadataAsync 尚未实现");
        return Task.FromResult(new SourceMetadata(0, null, null));
    }

    public Task<ExamUploadReq?> GetByExamIdAsync(string orgCode, string examId)
    {
        // TODO: 待源表结构确认后实现
        _logger.LogWarning("SqlServerSource.GetByExamIdAsync 尚未实现");
        return Task.FromResult<ExamUploadReq?>(null);
    }

    public async IAsyncEnumerable<ExamUploadReq> EnumerateArchivesAsync(
        DateRange range, int pageSize, [EnumeratorCancellation] CancellationToken ct)
    {
        // TODO: 待源表结构确认后实现
        _logger.LogWarning("SqlServerSource.EnumerateArchivesAsync 尚未实现");
        await Task.CompletedTask;
        yield break;
    }
}
