using DataMigrate.Models;

namespace DataMigrate.Core;

/// <summary>数据源适配接口（Strategy 模式）</summary>
public interface IMigrationSource
{
    string SourceName { get; }
    Task ValidateAsync();
    Task<SourceMetadata> GetMetadataAsync(DateRange range);
    Task<ExamUploadReq?> GetByExamIdAsync(string orgCode, string examId);
    IAsyncEnumerable<ExamUploadReq> EnumerateArchivesAsync(
        DateRange range, int pageSize, CancellationToken ct);
}
