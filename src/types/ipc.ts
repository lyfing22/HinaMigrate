// Sidecar stdout 事件类型(与 C# IpcWriter/IpcLogSink 输出的 camelCase JSON 对应)

export type LogLevel = 'DBG' | 'INF' | 'WRN' | 'ERR' | 'FTL'

export interface LogEvent {
  type: 'log'; level: LogLevel; message: string; ts: string; error?: string
}
export interface ProgressEvent {
  type: 'progress'; total: number; processed: number
  succeeded: number; failed: number; elapsedMs: number; rps: number
}
export interface StartedEvent {
  type: 'started'; id: string; label: string; mode?: string; count?: number
}
export interface ResultEvent {
  type: 'result'; id: string; ok: boolean
  label?: string; mode?: string; summary?: string; message?: string
  total?: number; succeeded?: number; failed?: number; elapsedMs?: number
  data?: unknown
}
export interface ErrorEvent { type: 'error'; message?: string; raw?: string }
export interface ConsoleEvent { type: 'console'; text?: string; raw?: string }
export interface DebugEvent { type: 'debug'; archive: unknown }

export type SidecarEvent =
  | LogEvent | ProgressEvent | StartedEvent | ResultEvent
  | ErrorEvent | ConsoleEvent | DebugEvent

export interface TerminatedStatus { type: 'terminated'; code?: number; signal?: number }

export interface SupportedTypes { source: string[]; dest: string[] }

export interface PlanRow {
  id: string; dbFlag: string
  startTime: string; endTime: string
  totalRecords?: number; successCount?: number; failedCount?: number
  status: string; msg?: string
}
export interface ErrorRow {
  id: string; dbFlag: string; importTime: string; errorMessage?: string
}
