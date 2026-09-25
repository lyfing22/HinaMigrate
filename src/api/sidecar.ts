import { invoke } from '@tauri-apps/api/core'
import { listen, type UnlistenFn } from '@tauri-apps/api/event'
import type {
  SidecarEvent, ResultEvent, TerminatedStatus, SupportedTypes, PlanRow, ErrorRow
} from '@/types/ipc'

let seq = 0
const pending = new Map<string, { resolve: (v: ResultEvent) => void; reject: (e: Error) => void }>()

let inited = false
let unlistenEvent: UnlistenFn | null = null
let unlistenStatus: UnlistenFn | null = null

export interface SidecarHandlers {
  onEvent: (ev: SidecarEvent) => void
  onStatus: (s: TerminatedStatus) => void
}

/** 启动 sidecar 子进程。 */
export async function sidecarStart(): Promise<void> {
  await invoke('sidecar_start')
}

/** 向 sidecar stdin 写一条命令(JSON)。Rust 侧自动补 \n。 */
export async function sidecarSend(cmd: string, args: Record<string, unknown> = {}): Promise<ResultEvent> {
  const id = `r${++seq}`
  const line = JSON.stringify({ id, cmd, args })
  await invoke('sidecar_send', { cmd: line })
  return new Promise<ResultEvent>((resolve, reject) => {
    pending.set(id, { resolve, reject })
    // 兜底超时(30s)，避免同步命令无响应时永久挂起
    setTimeout(() => {
      if (pending.has(id)) {
        pending.delete(id)
        reject(new Error('命令超时未响应'))
      }
    }, 30_000)
  })
}

export async function sidecarStop(): Promise<void> {
  await invoke('sidecar_stop')
}

export async function sidecarRunning(): Promise<boolean> {
  return invoke<boolean>('sidecar_running')
}

/** 订阅 sidecar 事件流(幂等，重复调用不会重复订阅)。 */
export async function initSidecarListener(h: SidecarHandlers): Promise<void> {
  if (inited) return
  inited = true
  unlistenEvent = await listen<SidecarEvent>('sidecar://event', e => {
    const ev = e.payload
    // 带有 id 且匹配待处理请求：started/result 解除对应 Promise
    const id = (ev as { id?: string }).id
    if (id && pending.has(id)) {
      if (ev.type === 'started') {
        // 后台命令已启动：提前 resolve，后续 result 作为事件流入 runStore
        const p = pending.get(id)!
        pending.delete(id)
        p.resolve(ev as unknown as ResultEvent)
      } else if (ev.type === 'result') {
        const p = pending.get(id)!
        pending.delete(id)
        if (ev.ok) p.resolve(ev)
        else p.reject(new Error(ev.message ?? ev.summary ?? '失败'))
      } else {
        h.onEvent(ev)
      }
    } else {
      h.onEvent(ev)
    }
  })
  unlistenStatus = await listen<TerminatedStatus>('sidecar://status', e => h.onStatus(e.payload))
}

export function disposeSidecarListener() {
  unlistenEvent?.(); unlistenStatus?.()
  inited = false
}

// ── 高层命令封装 ──

export const cmdSupportedTypes = () => sidecarSend('supportedTypes').then(r => r.data as SupportedTypes)
export const cmdPing = (side: 'source' | 'dest', connStr: string) =>
  sidecarSend('ping', { side, connStr }).then(r => r.data as { ok: boolean; message: string })
export const cmdInitDb = (profile: unknown) =>
  sidecarSend('initDb', { profile }).then(r => r.data as { ok: boolean; message: string })
export const cmdGetPlans = (connStr: string, dbFlag: string) =>
  sidecarSend('getPlans', { connStr, dbFlag }).then(r => r.data as { plans: PlanRow[] })
export const cmdGetErrors = (connStr: string, dbFlag: string) =>
  sidecarSend('getErrors', { connStr, dbFlag }).then(r => r.data as { errors: ErrorRow[] })
export const cmdStart = (profile: unknown) =>
  sidecarSend('start', { profile }) // resolve on 'started'
export const cmdRetryErrors = (profile: unknown, ids: string[]) =>
  sidecarSend('retryErrors', { profile, ids }) // resolve on 'started'
