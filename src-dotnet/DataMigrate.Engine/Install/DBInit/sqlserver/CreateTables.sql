-- ============================================================
-- FJYXHR 数据迁移工具 - 目标数据库初始化脚本（SQL Server）
-- 所有 DDL 均使用幂等写法，可重复执行
-- ============================================================

-- ============================================================
-- ZTemp_MigratePlan: 迁移计划追踪表
--   - 每次批量启动时按 PlanIntervalDays 切割写入多条记录
--   - Status: Pending / Running / Completed / Failed
--   - Msg 仅记录备注、异常摘要或失败原因摘要
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
               WHERE TABLE_NAME = 'ZTemp_MigratePlan')
BEGIN
    CREATE TABLE ZTemp_MigratePlan (
        Id            UNIQUEIDENTIFIER NOT NULL,
        DbFlag        NVARCHAR(64)     NOT NULL,
        StartTime     DATETIME         NOT NULL,
        EndTime       DATETIME         NOT NULL,
        TotalRecords  INT              NULL,
        SuccessCount  INT              NULL,
        FailedCount   INT              NULL,
        Status        NVARCHAR(32)     NOT NULL DEFAULT N'Pending',
        Msg           NVARCHAR(512)    NULL,
        CONSTRAINT PK_ZTemp_MigratePlan PRIMARY KEY (Id, DbFlag)
    );
END

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
           WHERE TABLE_NAME = 'ZTemp_MigratePlan')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                   WHERE TABLE_NAME = 'ZTemp_MigratePlan'
                     AND COLUMN_NAME = 'Status')
    BEGIN
        ALTER TABLE ZTemp_MigratePlan
        ADD Status NVARCHAR(32) NOT NULL DEFAULT N'Pending';
    END

    UPDATE ZTemp_MigratePlan
    SET Status = N'Completed'
    WHERE IsSuccess = 1;

    UPDATE ZTemp_MigratePlan
    SET Status = N'Failed'
    WHERE IsSuccess = 0;

    UPDATE ZTemp_MigratePlan
    SET Status = N'Pending'
    WHERE Status IS NULL OR Status = N'';

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'ZTemp_MigratePlan'
                 AND COLUMN_NAME = 'IsSuccess')
    BEGIN
        ALTER TABLE ZTemp_MigratePlan DROP COLUMN IsSuccess;
    END
END

-- ============================================================
-- ZTemp_MigrateError: 失败记录缓冲表
--   - ExamUploader 批量写入失败原因，供 retry 模式重试使用
--   - 成功记录自动清除（DELETE）
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
               WHERE TABLE_NAME = 'ZTemp_MigrateError')
BEGIN
    CREATE TABLE ZTemp_MigrateError (
        Id            NVARCHAR(128)   NOT NULL,
        DbFlag        NVARCHAR(64)    NOT NULL,
        ArchiveType   NVARCHAR(64)    NOT NULL DEFAULT N'ExamApproval',
        ImportTime    DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
        ErrorMessage  NVARCHAR(MAX)   NULL,
        CONSTRAINT PK_ZTemp_MigrateError PRIMARY KEY (Id, DbFlag)
    );
END
