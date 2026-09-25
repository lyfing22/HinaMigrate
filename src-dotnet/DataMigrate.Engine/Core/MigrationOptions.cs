using DataMigrate.Models;

namespace DataMigrate.Core;

public class MigrationOptions
{
    public ConnectionStringsOption ConnectionStrings { get; init; } = new();
    public MigrationConfig Migration { get; init; } = new();
    public UploadConfig Upload { get; init; } = new();
}

public class ConnectionStringsOption
{
    public string SourceConn { get; init; } = "";
    public string DestinationConn { get; init; } = "";
}

public class MigrationConfig
{
    /// <summary>
    /// 运行模式。合法值：
    ///   batch - 全量迁移
    ///   debug - 单条调试
    ///   retry - 错误重试
    /// </summary>
    public string Mode { get; init; } = "batch";
    public string? ExamId { get; init; }
    public DateRangeConfig TimeRange { get; init; } = new();
    public int PageSize { get; init; } = 100;
    public int Parallelism { get; init; } = 4;
    public int ZTempBatchSize { get; init; } = 100;
    public string DbFlag { get; init; } = "";
    /// <summary>按此天数切割迁移计划，默认每天一条</summary>
    public int PlanIntervalDays { get; init; } = 1;
    /// <summary>同时运行的子计划最大并发数</summary>
    public int MaxParallelPlans { get; init; } = 2;
    /// <summary>子计划执行顺序：ascending(早→晚) / descending(晚→早)</summary>
    public string PlanOrder { get; init; } = "ascending";
}

public class DateRangeConfig
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}

public class UploadConfig
{
    public string Url { get; init; } = "";
    public string JwtAppId { get; init; } = "";
    /// <summary>JWT serverNode，与 JwtAppId 相同场景可留空（空则复用 JwtAppId）</summary>
    public string JwtServerNode { get; init; } = "";
    public string JwtAppSecret { get; init; } = "";
    public int JwtExpiryMinutes { get; init; } = 40;
    public int TimeoutSeconds { get; init; } = 30;
}

public record DateRange(DateTime Start, DateTime End);
public record SourceMetadata(int EstimatedCount, DateTime? MinDate, DateTime? MaxDate);
public record PlanInfo(Guid Id, DateRange Range);
