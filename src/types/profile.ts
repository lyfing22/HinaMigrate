// 镜像 C# MigrationOptions(camelCase)，与 sidecar 反序列化大小写不敏感一致

export type MigrationMode = 'batch' | 'debug' | 'retry'
export type PlanOrder = 'ascending' | 'descending'

export interface ConnectionStrings {
  sourceConn: string
  destinationConn: string
}

export interface MigrationConfig {
  mode: MigrationMode
  examId?: string
  timeRange: { start: string; end: string }
  pageSize: number
  parallelism: number
  zTempBatchSize: number
  dbFlag: string
  planIntervalDays: number
  maxParallelPlans: number
  planOrder: PlanOrder
}

export interface UploadConfig {
  url: string
  jwtAppId: string
  jwtServerNode: string
  jwtAppSecret: string
  jwtExpiryMinutes: number
  timeoutSeconds: number
}

export interface MigrationOptions {
  connectionStrings: ConnectionStrings
  migration: MigrationConfig
  upload: UploadConfig
}

// 系统默认配置：与 sidecar appsettings.json 保持一致，
// 首次启动无配置档时自动写入，用户可在此基础上按需覆盖。
export function defaultProfile(): MigrationOptions {
  return {
    connectionStrings: {
      sourceConn:
        'DatabaseType=mongodb;Host=192.168.3.246;Port=27017;Database=NewRIS;User=ris_admin;Password=hinacom;serverSelectionTimeoutMS=5000',
      destinationConn:
        'DatabaseType=kingbase;Server=192.168.1.165;Port=54321;Database=miPlatform_MIIS_Upload;User=sa;Password=Hin@c0m.c)m;Connect Timeout=60;TrustServerCertificate=true'
    },
    migration: {
      mode: 'batch',
      examId: 'M761|Exam2026622724',
      timeRange: { start: '2026-07-09', end: '2026-08-29' },
      pageSize: 100,
      parallelism: 4,
      zTempBatchSize: 100,
      dbFlag: 'FJNew',
      planIntervalDays: 1,
      maxParallelPlans: 2,
      planOrder: 'ascending'
    },
    upload: {
      url: 'http://192.168.1.165/api/Exam/Upload',
      jwtAppId: '100A1002',
      jwtServerNode: '',
      jwtAppSecret: 'fTHLbVUMyEmn3w0fuBmysg==',
      jwtExpiryMinutes: 40,
      timeoutSeconds: 30
    }
  }
}
