import { invoke } from '@tauri-apps/api/core'
import type { MigrationOptions } from '@/types/profile'

export interface ProfileMeta { name: string }

export const profilesApi = {
  list: () => invoke<ProfileMeta[]>('list_profiles'),
  load: (name: string) => invoke<MigrationOptions>('load_profile', { name }),
  save: (name: string, profile: MigrationOptions) => invoke<void>('save_profile', { name, profile }),
  delete: (name: string) => invoke<void>('delete_profile', { name }),
  appDataDir: () => invoke<string>('get_app_data_dir')
}
