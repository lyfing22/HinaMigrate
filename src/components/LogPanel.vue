<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { Select, SelectOption, Button, Tag, Empty } from 'ant-design-vue'
import type { LogEntry } from '@/stores/run'

const props = defineProps<{ logs: LogEntry[]; status: string }>()

const level = ref<'ALL' | 'INF' | 'WRN' | 'ERR' | 'DBG'>('ALL')
const scrollEl = ref<HTMLDivElement | null>(null)
const autoScroll = ref(true)

const filtered = computed(() =>
  level.value === 'ALL' ? props.logs : props.logs.filter(l => l.level === level.value)
)

const levelColor: Record<string, string> = {
  DBG: 'default', INF: 'blue', WRN: 'orange', ERR: 'red', FTL: 'red'
}

watch(() => props.logs.length, async () => {
  if (!autoScroll.value) return
  await nextTick()
  if (scrollEl.value) scrollEl.value.scrollTop = scrollEl.value.scrollHeight
})

function exportTxt() {
  const text = props.logs.map(l => `[${l.ts} ${l.level}] ${l.message}${l.error ? ' | ' + l.error : ''}`).join('\n')
  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `migrate-${Date.now()}.log`
  a.click()
  URL.revokeObjectURL(a.href)
}
</script>

<template>
  <div style="display: flex; flex-direction: column; height: 100%">
    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px">
      <Select v-model:value="level" size="small" style="width: 120px">
        <SelectOption value="ALL">全部</SelectOption>
        <SelectOption value="INF">INF</SelectOption>
        <SelectOption value="WRN">WRN</SelectOption>
        <SelectOption value="ERR">ERR</SelectOption>
        <SelectOption value="DBG">DBG</SelectOption>
      </Select>
      <div>
        <Button size="small" @click="autoScroll = !autoScroll">
          {{ autoScroll ? '暂停滚动' : '自动滚动' }}
        </Button>
        <Button size="small" @click="exportTxt" style="margin-left: 8px">导出</Button>
      </div>
    </div>
    <div ref="scrollEl" class="logbox">
      <Empty v-if="!filtered.length" description="暂无日志" style="margin-top: 40px" />
      <div v-for="(l, i) in filtered" :key="i" class="logline">
        <span class="ts">{{ l.ts }}</span>
        <Tag :color="levelColor[l.level] ?? 'default'" style="margin: 0 6px; min-width: 36px; text-align: center">{{ l.level }}</Tag>
        <span class="msg">{{ l.message }}</span>
        <span v-if="l.error" class="err"> | {{ l.error }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.logbox {
  flex: 1;
  overflow: auto;
  background: #1e1e1e;
  color: #d4d4d4;
  padding: 8px;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.6;
  border-radius: 4px;
}
.logline { white-space: pre-wrap; word-break: break-all; }
.ts { color: #888; margin-right: 6px; }
.msg { color: #d4d4d4; }
.err { color: #f48771; }
</style>
