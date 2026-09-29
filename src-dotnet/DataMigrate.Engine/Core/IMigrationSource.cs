using DataMigrate.Models;

namespace DataMigrate.Core;

/// <summary>数据源适配接口（Strategy 模式）</summary>
public interface IMigrationSource
{
    string SourceName { get; }
    Task ValidateAsync();
    Task<SourceMetadata> GetMetadataAsync(DateRange range);
    /// <summary>
    /// 按 ExamId 查询单条记录。调用方需传入可取消 token；源端实现应将其
    /// 透传到驱动查询（MongoDB / SQL / KingBase）以保证 Stop 按钮能真正生效，
    /// 否则源端查询慢时整个 retry/debug 流程会被无限期阻塞。
    /// </summary>
    Task<ExamUploadReq?> GetByExamIdAsync(string orgCode, string examId, CancellationToken ct = default);
    IAsyncEnumerable<ExamUploadReq> EnumerateArchivesAsync(
        DateRange range, int pageSize, CancellationToken ct);
}
