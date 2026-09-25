import { ref } from 'vue'
import { cmdSupportedTypes } from '@/api/sidecar'
import { profilesApi } from '@/api/profiles'
import type { SupportedTypes } from '@/types/ipc'

class MetaStore {
  readonly supported = ref<SupportedTypes>({ source: [], dest: [] })
  readonly appDataDir = ref('')

  async load() {
    try { this.supported.value = await cmdSupportedTypes() } catch { /* sidecar 未运行时忽略 */ }
    try { this.appDataDir.value = await profilesApi.appDataDir() } catch { /* ignore */ }
  }
}
export const metaStore = new MetaStore()
