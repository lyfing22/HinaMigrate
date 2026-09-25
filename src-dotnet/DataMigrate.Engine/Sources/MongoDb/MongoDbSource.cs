using System.Runtime.CompilerServices;
using DataMigrate.Core;
using DataMigrate.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DataMigrate.Sources.MongoDb;

/// <summary>
/// MongoDB 数据源实现：从 exam.approval 集合读取数据并转换为 ExamUploadReq
/// </summary>
public class MongoDbSource : IMigrationSource
{
    public string SourceName => "MongoDb";

    private readonly IMongoCollection<ExamEntity> _collection;
    private readonly ILogger<MongoDbSource> _logger;

    public MongoDbSource(string mongoConnectionString, ILogger<MongoDbSource> logger)
    {
        _logger = logger;
        var client = new MongoClient(mongoConnectionString);
        var database = client.GetDatabase("NewRIS");
        _collection = database.GetCollection<ExamEntity>("exam.approval");
    }

    public Task ValidateAsync()
    {
        try
        {
            _collection.Database.ListCollectionNames();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"MongoDB 连接失败: {ex.Message}", ex);
        }
    }

    public async Task<SourceMetadata> GetMetadataAsync(DateRange range)
    {
        var filter = Builders<ExamEntity>.Filter.And(
            Builders<ExamEntity>.Filter.Gte(e => e.CreateTime, range.Start),
            Builders<ExamEntity>.Filter.Lte(e => e.CreateTime, range.End));

        var count = (int)await _collection.CountDocumentsAsync(filter);

        DateTime? minDate = null;
        DateTime? maxDate = null;
        if (count > 0)
        {
            var first = await _collection.Find(filter).SortBy(e => e.CreateTime).Limit(1).FirstOrDefaultAsync();
            var last = await _collection.Find(filter).SortByDescending(e => e.CreateTime).Limit(1).FirstOrDefaultAsync();
            minDate = first?.CreateTime;
            maxDate = last?.CreateTime;
        }

        return new SourceMetadata(count, minDate, maxDate);
    }

    public async Task<ExamUploadReq?> GetByExamIdAsync(string orgCode, string examId)
    {
        var id = ExamEntity.GetId(orgCode, examId);
        var entity = await _collection.Find(e => e.Id == id).FirstOrDefaultAsync();
        return entity == null ? null : ConvertToUploadReq(entity);
    }

    public async IAsyncEnumerable<ExamUploadReq> EnumerateArchivesAsync(
        DateRange range, int pageSize, [EnumeratorCancellation] CancellationToken ct)
    {
        var filter = Builders<ExamEntity>.Filter.And(
            Builders<ExamEntity>.Filter.Gte(e => e.CreateTime, range.Start),
            Builders<ExamEntity>.Filter.Lte(e => e.CreateTime, range.End));

        var sort = Builders<ExamEntity>.Sort.Ascending(e => e.CreateTime);

        long totalSkipped = 0;
        long totalRead = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await _collection.Find(filter)
                .Sort(sort)
                .Skip((int)totalSkipped)
                .Limit(pageSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var entity in batch)
            {
                ct.ThrowIfCancellationRequested();
                var req = ConvertToUploadReq(entity);
                if (req != null)
                {
                    yield return req;
                    Interlocked.Increment(ref totalRead);
                }
            }

            totalSkipped += batch.Count;
            _logger.LogDebug("已读取 {Count} 条", totalSkipped);

            if (batch.Count < pageSize) break;
        }

        _logger.LogInformation("MongoDB 读取完成，共 {Count} 条", totalRead);
    }

    private ExamUploadReq? ConvertToUploadReq(ExamEntity entity)
    {
        if (entity.Order == null)
        {
            _logger.LogWarning("跳过无 Order 数据的记录: {Id}", entity.Id);
            return null;
        }

        // 验证 HisRequestDate
        if (!string.IsNullOrEmpty(entity.Order.HisRequestDate) &&
            !DateTime.TryParse(entity.Order.HisRequestDate, out _))
        {
            entity.Order.HisRequestDate = null;
        }

        return new ExamUploadReq
        {
            OrgCode = entity.OrgCode,
            OrgName = entity.OrgName,
            ExamId = entity.ExamId,
            Order = entity.Order,
            Visit = entity.Visit ?? new ExamVisitInfo(),
            Patient = entity.Patient ?? new ExamPatientInfo(),
            Report = entity.Report ?? new ExamReportInfo()
        };
    }
}
