// 统一连接串解析/拼装：与 C# ConnectionStringParser 的 key=value; 格式约定一致
// DatabaseType 作为判别字段；User 对三种库均兼容(kingbase 解析时映射为 Username)

export interface ConnFields {
  dbType: string
  host: string
  port: string
  database: string
  user: string
  password: string
  extra: string // 额外参数，原样透传(如 TrustServerCertificate=true;serverSelectionTimeoutMS=5000)
}

export function parseConnString(raw: string): ConnFields {
  const f: ConnFields = {
    dbType: '', host: '', port: '', database: '', user: '', password: '', extra: ''
  }
  if (!raw) return f
  const extras: string[] = []
  for (const seg of raw.split(';')) {
    const i = seg.indexOf('=')
    if (i < 0) continue
    const k = seg.slice(0, i).trim()
    const v = seg.slice(i + 1).trim()
    if (!k) continue
    const kl = k.toLowerCase()
    if (kl === 'databasetype') f.dbType = v
    else if (kl === 'host' || kl === 'server') f.host ||= v
    else if (kl === 'port') f.port ||= v
    else if (kl === 'database') f.database ||= v
    else if (kl === 'user' || kl === 'username' || kl === 'user id') f.user ||= v
    else if (kl === 'password') f.password ||= v
    else extras.push(`${k}=${v}`)
  }
  f.extra = extras.join(';')
  return f
}

export function buildConnString(f: ConnFields): string {
  const parts: string[] = []
  if (f.dbType) parts.push(`DatabaseType=${f.dbType}`)
  if (f.host) parts.push(`Host=${f.host}`)
  if (f.port) parts.push(`Port=${f.port}`)
  if (f.database) parts.push(`Database=${f.database}`)
  if (f.user) parts.push(`User=${f.user}`)
  if (f.password) parts.push(`Password=${f.password}`)
  if (f.extra) parts.push(f.extra)
  return parts.join(';')
}
