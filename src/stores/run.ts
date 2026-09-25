import { ref, shallowRef } from 'vue'
import {
  sidecarStart, sidecarStop, initSidecarListener, cmdStart, cmdRetryErrors, disposeSidecarListener
} from '@/api/sidecar'
import type { SidecarEvent, ProgressEvent, ResultEvent } from '@/types/ipc'
import type { MigrationOptions } from '@/types/profile'

export type RunStatus = 'idle' | 'running' | 'stopping' | 'done' | 'error'

export interface LogEntry { ts: string; level: string; message: string; error?: string }

const EMPTY_PROGRESS: ProgressEvent = {
  type: 'progress', total: 0, processed: 0, succeeded: 0, failed: 0, elapsedMs: 0, rps: 0
}

class RunStore {
  readonly status = ref<RunStatus>('idle')
  readonly label = ref('')
  readonly progress = ref<ProgressEvent>({ ...EMPTY_PROGRESS })
  readonly lastResult = ref<ResultEvent | null>(null)
  readonly debugArchive = shallowRef<unknown>(null)
  readonly logs = shallowRef<LogEntry[]>([])

  private cap = 5000
  private listening = false

  async ensureListening() {
    if (this.listening) return
    this.listening = true
    await initSidecarListener({
      onEvent: e => this.dispatch(e),
      onStatus: s => {
        if (s.type === 'terminated' && this.status.value === 'running') {
          this.status.value = 'idle'
        }
      }
    })
  }

  private pushLog(l: LogEntry) {
    const arr = this.logs.value
    arr.push(l)
    if (arr.length > this.cap) this.logs.value = arr.slice(-this.cap)
    else this.logs.value = [...arr]
  }

  /** 非 IPC 来源的日志注入（如启动失败提示），避免静默失败 */
  noteLog(message: string, level: 'ERR' | 'INF' = 'ERR') {
    this.pushLog({ ts: nowTs(), level, message })
  }

  private clearRun() {
    this.logs.value = []
    this.lastResult.value = null
    this.debugArchive.value = null
    this.progress.value = { ...EMPTY_PROGRESS }
  }

  async start(profile: MigrationOptions) {
    await this.ensureListening()
    this.clearRun()
    this.status.value = 'running'
    this.label.value = profile.migration.mode
    await sidecarStart()
    await cmdStart(profile) // resolve on 'started'
  }

  async retryErrors(profile: MigrationOptions, ids: string[]) {
    await this.ensureListening()
    this.clearRun()
    this.status.value = 'running'
    this.label.value = 'retry'
    await sidecarStart()
    await cmdRetryErrors(profile, ids)
  }

  async stop() {
    this.status.value = 'stopping'
    try { await sidecarStop() } catch { /* ignore */ }
  }

  private dispatch(ev: SidecarEvent) {
    switch (ev.type) {
      case 'log':
        this.pushLog({ ts: ev.ts, level: ev.level, message: ev.message, error: ev.error })
        break
      case 'console':
        this.pushLog({ ts: nowTs(), level: 'INF', message: ev.text ?? ev.raw ?? '' })
        break
      case 'progress':
        this.progress.value = ev
        break
      case 'debug':
        this.debugArchive.value = ev.archive
        this.pushLog({ ts: nowTs(), level: 'INF', message: '已回传单条调试数据，可在日志查看 JSON' })
        break
      case 'error':
        this.pushLog({ ts: nowTs(), level: 'ERR', message: ev.message ?? ev.raw ?? '错误' })
        if (this.status.value === 'running') this.status.value = 'error'
        break
      case 'result':
        this.lastResult.value = ev
        if (this.status.value !== 'stopping') {
          this.status.value = ev.ok ? 'done' : 'error'
        }
        break
      case 'started':
        break
    }
  }

  dispose() { disposeSidecarListener() }
}

function nowTs() {
  return new Date().toISOString().slice(11, 23)
}

export const runStore = new RunStore()
