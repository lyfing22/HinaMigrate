import { ref, computed } from 'vue'
import { profilesApi, type ProfileMeta } from '@/api/profiles'
import { defaultProfile, type MigrationOptions } from '@/types/profile'

class ProfileStore {
  readonly list = ref<ProfileMeta[]>([])
  readonly activeName = ref('')
  readonly profile = ref<MigrationOptions>(defaultProfile())
  readonly dirty = ref(false)

  readonly hasActive = computed(() => !!this.activeName.value)

  async loadAll() {
    this.list.value = await profilesApi.list()
    if (!this.list.value.find(m => m.name === this.activeName.value)) {
      if (this.list.value.length) await this.activate(this.list.value[0].name)
      else { this.activeName.value = ''; this.profile.value = defaultProfile() }
    }
  }

  async activate(name: string) {
    this.profile.value = await profilesApi.load(name)
    this.activeName.value = name
    this.dirty.value = false
  }

  async saveAs(name: string) {
    await profilesApi.save(name, this.profile.value)
    this.activeName.value = name
    this.dirty.value = false
    await this.loadAll()
  }

  async remove(name: string) {
    await profilesApi.delete(name)
    if (this.activeName.value === name) {
      this.list.value = this.list.value.filter(m => m.name !== name)
      if (this.list.value.length) await this.activate(this.list.value[0].name)
      else { this.activeName.value = ''; this.profile.value = defaultProfile() }
    } else {
      await this.loadAll()
    }
  }

  newProfile() {
    this.activeName.value = ''
    this.profile.value = defaultProfile()
    this.dirty.value = true
  }

  markDirty() { this.dirty.value = true }
}
export const profileStore = new ProfileStore()
