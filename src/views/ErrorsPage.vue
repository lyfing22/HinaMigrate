<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Card, Button, Table, Space, Input, Typography, message } from 'ant-design-vue'
import { runStore } from '@/stores/run'
import { profileStore } from '@/stores/profile'
import { sidecarStart, cmdGetErrors } from '@/api/sidecar'
import { validateProfile } from '@/utils/validate'
import type { ErrorRow } from '@/types/ipc'

const errors = ref<ErrorRow[]>([])
const loading = ref(false)
const selected = ref<ErrorRow[]>([])
const dbFlag = ref(profileStore.profile.value.migration.dbFlag)

const columns = [
  { title: 'ID', dataIndex: 'id', key: 'id', width: 280, ellipsis: true },
  { title: '数据标志', dataIndex: 'dbFlag', width: 100, key: 'dbFlag' },
  { title: '导入时间', dataIndex: 'importTime', key: 'importTime' },
  { title: '错误信息', dataIndex: 'errorMessage', key: 'errorMessage', ellipsis: true }
]

async function refresh(silent = false) {
  const connStr = profileStore.profile.value.connectionStrings.destinationConn
  if (!connStr) {
    if (silent) { console.warn('错误重试：未配置目标连接，跳过自动加载'); return }
    message.warning('请先在配置页填写目标连接信息'); return
  }
  if (!dbFlag.value.trim()) {
    if (silent) { console.warn('错误重试：未填写数据标志 (DbFlag)，跳过自动加载'); return }
    message.warning('请填写数据标志 (DbFlag)'); return
  }
  loading.value = true
  selected.value = []
  try {
    await runStore.ensureListening()
    try { await sidecarStart() } catch { /* 已运行 */ }
    const r = await cmdGetErrors(connStr, dbFlag.value)
    errors.value = r.errors
  } catch (e) {
    if (silent) { console.warn('错误重试：自动加载失败:', e); return }
    message.error(`查询失败: ${(e as Error).message}`)
  } finally {
    loading.value = false
  }
}

async function retrySelected() {
  if (!selected.value.length) { message.warning('请先勾选要重试的记录'); return }
  const ids = selected.value.map(e => e.id)
  // 构造 retry 模式的 profile 副本(mode=retry 以启用 BufferAll 策略)
  const profile = structuredClone(profileStore.profile.value)
  profile.migration.mode = 'retry'
  const errors = validateProfile(profile, 'retry')
  if (errors.length) {
    message.error({ content: errors.map(e => e.message).join('；'), duration: 5 })
    return
  }
  try {
    await runStore.retryErrors(profile, ids)
    message.success(`已提交 ${ids.length} 条重试`)
  } catch (e) {
    message.error(`重试失败: ${(e as Error).message}`)
  }
}

function onSelectionChange(_keys: (string | number)[], rows: ErrorRow[]) {
  selected.value = rows
}

onMounted(() => {
  dbFlag.value = profileStore.profile.value.migration.dbFlag
  refresh(true)
})
</script>

<template>
  <Card title="错误重试 (ZTemp_MigrateError)" size="small">
    <template #extra>
      <Space>
        <Input v-model:value="dbFlag" placeholder="数据标志 (DbFlag)" style="width: 180px" />
        <Button @click="refresh()" :loading="loading">刷新</Button>
        <Button type="primary" :disabled="!selected.length || runStore.status.value === 'running'"
          :loading="runStore.status.value === 'running' && runStore.label.value === 'retry'"
          @click="retrySelected">重试选中 ({{ selected.length }})</Button>
      </Space>
    </template>
    <Table :data-source="errors" :columns="columns" :loading="loading" row-key="id" size="small"
      :row-selection="{ selectedRowKeys: selected.map(e => e.id), onChange: onSelectionChange }"
      :pagination="{ pageSize: 20, showSizeChanger: true }">
      <template #emptyText>
        <Typography.Text type="secondary">暂无失败记录，可点击「刷新」重新查询</Typography.Text>
      </template>
    </Table>
  </Card>
</template>
