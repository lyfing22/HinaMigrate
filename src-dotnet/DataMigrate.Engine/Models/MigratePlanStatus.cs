namespace DataMigrate.Models;

/// <summary>ZTemp_MigratePlan 计划状态，仅保留生命周期必需状态。</summary>
public enum MigratePlanStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
