import { ref } from 'vue'
import { cmdSupportedTypes, sidecarStart } from '@/api/sidecar'
import { profilesApi } from '@/api/profiles'
import type { SupportedTypes } from '@/types/ipc'

class MetaStore {
  readonly supported = ref<SupportedTypes>({ source: [], dest: [] })
  readonly appDataDir = ref('')

  async load() {
    // 确保 sidecar 已启动后再拉取 supportedTypes；已运行则幂等返回
    try { await sidecarStart() } catch (e) { console.warn('[metaStore] sidecar 启动失败:', e) }
    try { this.supported.value = await cmdSupportedTypes() }
    catch (e) { console.warn('[metaStore] 获取 supportedTypes 失败:', e) }
    try { this.appDataDir.value = await profilesApi.appDataDir() }
    catch (e) { console.warn('[metaStore] 获取 appDataDir 失败:', e) }
  }
}
export const metaStore = new MetaStore()
