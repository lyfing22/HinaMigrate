-- ============================================================
-- FJYXHR 数据迁移工具 - 目标数据库初始化脚本（KingbaseES）
-- 所有 DDL 均使用幂等写法，可重复执行
-- ============================================================

-- 确保 uuid-ossp 扩展可用（生成 UUID 默认值）
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================
-- ZTemp_MigratePlan: 迁移计划追踪表
--   - 每次批量启动时按 PlanIntervalDays 切割写入多条记录
--   - Status: Pending / Running / Completed / Failed
--   - Msg 仅记录备注、异常摘要或失败原因摘要
-- ============================================================
CREATE TABLE IF NOT EXISTS ZTemp_MigratePlan (
    Id            UUID          NOT NULL DEFAULT uuid_generate_v4(),
    DbFlag        VARCHAR(64)   NOT NULL,
    StartTime     TIMESTAMP     NOT NULL,
    EndTime       TIMESTAMP     NOT NULL,
    TotalRecords  INT           NULL,
    SuccessCount  INT           NULL,
    FailedCount   INT           NULL,
    Status        VARCHAR(32)   NOT NULL DEFAULT 'Pending',
    Msg           VARCHAR(512)  NULL,
    CONSTRAINT PK_ZTemp_MigratePlan PRIMARY KEY (Id, DbFlag)
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public'
                 AND table_name = 'ZTemp_MigratePlan'
                 AND column_name = 'IsSuccess') THEN
        UPDATE public.ZTemp_MigratePlan
        SET Status = 'Completed'
        WHERE "IsSuccess" = TRUE;

        UPDATE public.ZTemp_MigratePlan
        SET Status = 'Failed'
        WHERE "IsSuccess" = FALSE;
    END IF;

    UPDATE public.ZTemp_MigratePlan
    SET Status = 'Pending'
    WHERE Status IS NULL OR Status = '';

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public'
                 AND table_name = 'ZTemp_MigratePlan'
                 AND column_name = 'IsSuccess') THEN
        ALTER TABLE public.ZTemp_MigratePlan DROP COLUMN "IsSuccess";
    END IF;
END
$$;

-- ============================================================
-- ZTemp_MigrateError: 失败记录缓冲表
--   - ExamUploader 批量写入失败原因，供 retry 模式重试使用
--   - 成功记录自动清除（DELETE）
-- ============================================================
CREATE TABLE IF NOT EXISTS ZTemp_MigrateError (
    Id            VARCHAR(128)  NOT NULL,
    DbFlag        VARCHAR(64)   NOT NULL,
    ArchiveType   VARCHAR(64)   NOT NULL DEFAULT 'ExamApproval',
    ImportTime    TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ErrorMessage  VARCHAR       NULL,
    CONSTRAINT PK_ZTemp_MigrateError PRIMARY KEY (Id, DbFlag)
);
