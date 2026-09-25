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

export function defaultProfile(): MigrationOptions {
  return {
    connectionStrings: { sourceConn: '', destinationConn: '' },
    migration: {
      mode: 'batch',
      timeRange: { start: '2020-01-01', end: '2030-12-31' },
      pageSize: 100,
      parallelism: 4,
      zTempBatchSize: 100,
      dbFlag: 'FJNew',
      planIntervalDays: 1,
      maxParallelPlans: 2,
      planOrder: 'ascending'
    },
    upload: {
      url: '',
      jwtAppId: '',
      jwtServerNode: '',
      jwtAppSecret: '',
      jwtExpiryMinutes: 40,
      timeoutSeconds: 30
    }
  }
}
