using DataMigrate.Core;

namespace DataMigrate.Sidecar.Ipc;

/// <summary>ZTemp_MigratePlan 行（前端计划追踪页）。</summary>
public sealed class PlanRow
{
    public string Id { get; set; } = "";
    public string DbFlag { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int? TotalRecords { get; set; }
    public int? SuccessCount { get; set; }
    public int? FailedCount { get; set; }
    public string Status { get; set; } = "";
    public string? Msg { get; set; }
}

/// <summary>ZTemp_MigrateError 行（前端错误重试页）。</summary>
public sealed class ErrorRow
{
    public string Id { get; set; } = "";
    public string DbFlag { get; set; } = "";
    public DateTime ImportTime { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Sidecar 启动时上报的支持库类型。</summary>
public sealed class SupportedTypes
{
    public IReadOnlyCollection<string> Source { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Dest { get; set; } = Array.Empty<string>();
}
