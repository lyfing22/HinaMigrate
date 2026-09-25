-- ============================================================
-- FJYXHR 数据迁移工具 - 目标数据库初始化脚本（达梦 DM8）
-- 所有 DDL 均使用幂等写法，可重复执行
-- 兼容 DM8 SP4+（支持 CREATE TABLE IF NOT EXISTS）
-- ============================================================

-- ============================================================
-- ZTemp_MigratePlan: 迁移计划追踪表
--   - 每次批量启动时按 PlanIntervalDays 切割写入多条记录
--   - Status: Pending / Running / Completed / Failed
--   - Msg 仅记录备注、异常摘要或失败原因摘要
-- ============================================================
CREATE TABLE IF NOT EXISTS ZTemp_MigratePlan (
    Id            VARCHAR(36)   NOT NULL,
    DbFlag        VARCHAR(64)   NOT NULL,
    StartTime     DATETIME      NOT NULL,
    EndTime       DATETIME      NOT NULL,
    TotalRecords  INT           NULL,
    SuccessCount  INT           NULL,
    FailedCount   INT           NULL,
    Status        VARCHAR(32)   NOT NULL DEFAULT 'Pending',
    Msg           VARCHAR(512)  NULL,
    CONSTRAINT PK_ZTemp_MigratePlan PRIMARY KEY (Id, DbFlag)
);

-- 达梦不支持 ALTER TABLE ADD COLUMN IF NOT EXISTS，迁移脚本使用动态 SQL 保证幂等。
BEGIN
    IF NOT EXISTS (SELECT 1 FROM ALL_TAB_COLUMNS
                   WHERE OWNER = 'MIPLATFORM'
                     AND TABLE_NAME = 'ZTEMP_MIGRATEPLAN'
                     AND COLUMN_NAME = 'STATUS') THEN
        EXECUTE IMMEDIATE 'ALTER TABLE ZTemp_MigratePlan ADD Status VARCHAR(32) NOT NULL DEFAULT ''Pending''';
    END IF;
END;
/

BEGIN
    IF EXISTS (SELECT 1 FROM ALL_TAB_COLUMNS
               WHERE OWNER = 'MIPLATFORM'
                 AND TABLE_NAME = 'ZTEMP_MIGRATEPLAN'
                 AND COLUMN_NAME = 'ISSUCCESS') THEN
        EXECUTE IMMEDIATE 'UPDATE ZTemp_MigratePlan SET Status = ''Completed'' WHERE IsSuccess = 1';
        EXECUTE IMMEDIATE 'UPDATE ZTemp_MigratePlan SET Status = ''Failed'' WHERE IsSuccess = 0';
        COMMIT;
    END IF;
END;
/

BEGIN
    IF EXISTS (SELECT 1 FROM ALL_TAB_COLUMNS
               WHERE OWNER = 'MIPLATFORM'
                 AND TABLE_NAME = 'ZTEMP_MIGRATEPLAN'
                 AND COLUMN_NAME = 'ISSUCCESS') THEN
        EXECUTE IMMEDIATE 'ALTER TABLE ZTemp_MigratePlan DROP COLUMN IsSuccess';
    END IF;
END;
/

-- ============================================================
-- ZTemp_MigrateError: 失败记录缓冲表
--   - ExamUploader 批量写入失败原因，供 retry 模式重试使用
--   - 成功记录自动清除（DELETE）
-- ============================================================
CREATE TABLE IF NOT EXISTS ZTemp_MigrateError (
    Id            VARCHAR(128)  NOT NULL,
    DbFlag        VARCHAR(64)   NOT NULL,
    ArchiveType   VARCHAR(64)   NOT NULL DEFAULT 'ExamApproval',
    ImportTime    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ErrorMessage  CLOB          NULL,
    CONSTRAINT PK_ZTemp_MigrateError PRIMARY KEY (Id, DbFlag)
);
