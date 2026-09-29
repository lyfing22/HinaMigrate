// 配置项合法性校验：与 C# ConfigValidator 的必填字段检查保持一致，
// 在前端提供即时反馈，避免把无效配置下发到 sidecar 才报错。

import type { MigrationOptions } from '@/types/profile'
import { parseConnString } from '@/api/connString'

export interface FieldError {
  /** 字段路径，如 'connectionStrings.sourceConn' */
  field: string
  /** 面向用户的错误消息 */
  message: string
}

/**
 * 单个连接串校验：按 DatabaseType 决定是否强制要求 Database。
 * - sqlserver / kingbase：Database 必填（C# 侧连接串需要）
 * - mongodb：C# MongoDbSource 内部硬编码 NewRIS，连接串缺 Database 也能连
 */
function pushConnErrors(errors: FieldError[], side: 'source' | 'dest', raw: string, f: ReturnType<typeof parseConnString>) {
  const prefix = side === 'source' ? '源端连接串' : '目标端连接串'
  const field = side === 'source' ? 'connectionStrings.sourceConn' : 'connectionStrings.destinationConn'
  const hostLabel = side === 'source' ? 'Host/Server' : 'Server/Host'
  if (!raw) {
    errors.push({ field, message: `${prefix}不能为空` })
    return
  }
  if (!f.dbType) {
    errors.push({ field, message: `${prefix}缺少 DatabaseType` })
  }
  if (!f.host) {
    errors.push({ field, message: `${prefix}缺少 ${hostLabel}` })
  }
  // MongoDB 场景下连接串 Database 可选（C# 侧硬编码），仅 sqlserver/kingbase 强制
  const needsDb = !!f.dbType && f.dbType.toLowerCase() !== 'mongodb'
  if (needsDb && !f.database) {
    errors.push({ field, message: `${prefix}缺少 Database` })
  }
}

/**
 * 校验整个配置档。
 * @param profile 待校验的配置
 * @param mode    可选，指定运行模式（默认取 profile.migration.mode）
 *                RunPage 切换模式后用于只校验当前模式相关字段。
 * @returns 错误列表，为空表示全部通过
 */
export function validateProfile(
  profile: MigrationOptions,
  mode?: string
): FieldError[] {
  const errors: FieldError[] = []
  const m = mode ?? profile.migration.mode

  // ── 连接串 ──
  const src = parseConnString(profile.connectionStrings.sourceConn)
  pushConnErrors(errors, 'source', profile.connectionStrings.sourceConn, src)
  const dst = parseConnString(profile.connectionStrings.destinationConn)
  pushConnErrors(errors, 'dest', profile.connectionStrings.destinationConn, dst)

  // ── 上传配置 ──
  if (!profile.upload.url) {
    errors.push({ field: 'upload.url', message: '上传地址不能为空' })
  } else if (!/^https?:\/\/.+/.test(profile.upload.url)) {
    errors.push({ field: 'upload.url', message: '上传地址需以 http:// 或 https:// 开头' })
  }
  if (!profile.upload.jwtAppId) {
    errors.push({ field: 'upload.jwtAppId', message: '应用ID不能为空' })
  }
  if (!profile.upload.jwtAppSecret) {
    errors.push({ field: 'upload.jwtAppSecret', message: '应用密钥不能为空' })
  }

  // ── 数值参数 ──
  const nums: [string, number, string][] = [
    ['migration.pageSize', profile.migration.pageSize, '分页大小'],
    ['migration.parallelism', profile.migration.parallelism, '并发数'],
    ['migration.zTempBatchSize', profile.migration.zTempBatchSize, '临时表批次'],
    ['migration.planIntervalDays', profile.migration.planIntervalDays, '计划间隔'],
    ['migration.maxParallelPlans', profile.migration.maxParallelPlans, '最大并发计划']
  ]
  for (const [field, val, label] of nums) {
    if (!Number.isInteger(val) || val < 1) {
      errors.push({ field, message: `${label}必须为 ≥ 1 的整数` })
    }
  }

  // ── 运行模式相关 ──
  if (m === 'batch' || m === 'retry') {
    const { start, end } = profile.migration.timeRange
    if (!start || !end) {
      errors.push({ field: 'migration.timeRange', message: '时间范围开始/结束不能为空' })
    } else if (start >= end) {
      errors.push({ field: 'migration.timeRange', message: '时间范围开始时间必须早于结束时间' })
    }
  }
  if (m === 'debug' && !profile.migration.examId?.trim()) {
    errors.push({ field: 'migration.examId', message: 'debug 模式需填写 ExamId（格式 OrgCode|ExamId）' })
  }

  // ── 数据标志 ──
  if (!profile.migration.dbFlag?.trim()) {
    errors.push({ field: 'migration.dbFlag', message: '数据标志不能为空' })
  }

  return errors
}

/**
 * 校验单个连接串字段，用于 ConnStringForm 的内联提示。
 * @returns 错误消息，为空表示合法
 */
export function validateConnString(raw: string, side: 'source' | 'dest'): string {
  if (!raw?.trim()) return side === 'source' ? '源端连接串不能为空' : '目标端连接串不能为空'
  const f = parseConnString(raw)
  if (!f.dbType) return '缺少 DatabaseType'
  if (!f.host) return '缺少 Host / Server'
  // MongoDB 硬编码 DB，不强制要求
  const needsDb = f.dbType.toLowerCase() !== 'mongodb'
  if (needsDb && !f.database) return '缺少 Database'
  if (!f.user) return '缺少 User（用户名）'
  return ''
}

/**
 * 按字段路径聚合错误，方便模板中查找单个字段的错误消息。
 */
export function errorsByField(errors: FieldError[]): Record<string, string> {
  const map: Record<string, string> = {}
  for (const e of errors) map[e.field] = e.message
  return map
}
