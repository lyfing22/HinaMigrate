<script setup lang="ts">
import { onMounted } from 'vue'
import { MainLayout } from '@/layouts'
import { runStore } from '@/stores/run'
import { sidecarStart } from '@/api/sidecar'

// App 挂载时立即拉起 sidecar(避免首次命令延迟,并尽早暴露启动失败)
onMounted(async () => {
  await runStore.ensureListening()
  try {
    await sidecarStart()
    runStore.noteLog('sidecar 已就绪', 'INF')
  } catch (e) {
    runStore.noteLog(`sidecar 启动失败：${e}`)
    console.error('[App] sidecar 启动失败:', e)
  }
})
</script>

<template>
  <MainLayout />
</template>
