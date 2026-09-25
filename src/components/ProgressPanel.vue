<script setup lang="ts">
import { computed } from 'vue'
import { Progress, Statistic, Row, Col, Typography } from 'ant-design-vue'
import type { ProgressEvent } from '@/types/ipc'
import { runStore } from '@/stores/run'

const props = defineProps<{ progress: ProgressEvent; status: string }>()

const pct = computed(() => props.progress.total > 0
  ? Math.min(100, (props.progress.processed / props.progress.total) * 100)
  : 0)
const elapsedFmt = computed(() => {
  const ms = props.progress.elapsedMs
  const s = Math.floor(ms / 1000)
  const hh = String(Math.floor(s / 3600)).padStart(2, '0')
  const mm = String(Math.floor((s % 3600) / 60)).padStart(2, '0')
  const ss = String(s % 60).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
})
</script>

<template>
  <div>
    <Progress :percent="pct" :status="status === 'error' ? 'exception' : status === 'done' ? 'success' : 'active'" />
    <Row :gutter="16" style="margin-top: 12px">
      <Col :span="6"><Statistic title="总数" :value="progress.total" /></Col>
      <Col :span="6"><Statistic title="已处理" :value="progress.processed" /></Col>
      <Col :span="6"><Statistic title="成功" :value="progress.succeeded" :value-style="{ color: '#3f8600' }" /></Col>
      <Col :span="6"><Statistic title="失败" :value="progress.failed" :value-style="{ color: '#cf1322' }" /></Col>
    </Row>
    <Row :gutter="16" style="margin-top: 12px">
      <Col :span="12"><Statistic title="耗时" :value="elapsedFmt" /></Col>
      <Col :span="12"><Statistic title="速度(条/秒)" :value="progress.rps" :precision="1" /></Col>
    </Row>
    <Typography v-if="runStore.lastResult.value" type="secondary" style="margin-top: 8px">
      结果: {{ runStore.lastResult.value?.summary }}
    </Typography>
  </div>
</template>
