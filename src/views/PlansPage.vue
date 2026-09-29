<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Card, Button, Table, Tag, Space, Input, Typography, message } from 'ant-design-vue'
import { runStore } from '@/stores/run'
import { profileStore } from '@/stores/profile'
import { sidecarStart, cmdGetPlans } from '@/api/sidecar'
import type { PlanRow } from '@/types/ipc'

const plans = ref<PlanRow[]>([])
const loading = ref(false)
const dbFlag = ref(profileStore.profile.value.migration.dbFlag)

const columns = [
  { title: '状态', dataIndex: 'status', width: 100, key: 'status' },
  { title: '开始时间', dataIndex: 'startTime', key: 'startTime' },
  { title: '结束时间', dataIndex: 'endTime', key: 'endTime' },
  { title: '总数', dataIndex: 'totalRecords', width: 80, key: 'total' },
  { title: '成功', dataIndex: 'successCount', width: 80, key: 'ok' },
  { title: '失败', dataIndex: 'failedCount', width: 80, key: 'fail' },
  { title: '信息', dataIndex: 'msg', key: 'msg', ellipsis: true }
]

function statusColor(s: string) {
  return { Pending: 'default', Running: 'processing', Completed: 'success', Failed: 'error' }[s] ?? 'default'
}

async function refresh() {
  const connStr = profileStore.profile.value.connectionStrings.destinationConn
  if (!connStr) { message.warning('请先在配置页填写目标连接信息'); return }
  if (!dbFlag.value.trim()) { message.warning('请填写数据标志 (DbFlag)'); return }
  loading.value = true
  try {
    await runStore.ensureListening()
    try { await sidecarStart() } catch { /* 已运行 */ }
    const r = await cmdGetPlans(connStr, dbFlag.value)
    plans.value = r.plans
  } catch (e) {
    message.error(`查询失败: ${(e as Error).message}`)
  } finally {
    loading.value = false
  }
}

onMounted(() => { dbFlag.value = profileStore.profile.value.migration.dbFlag })
</script>

<template>
  <Card title="计划追踪 (ZTemp_MigratePlan)" size="small">
    <template #extra>
      <Space>
        <Input v-model:value="dbFlag" placeholder="数据标志 (DbFlag)" style="width: 180px" />
        <Button @click="refresh" :loading="loading">刷新</Button>
      </Space>
    </template>
    <Table :data-source="plans" :columns="columns" :loading="loading" row-key="id" size="small"
      :pagination="{ pageSize: 20, showSizeChanger: true }">
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'status'">
          <Tag :color="statusColor(record.status)">{{ record.status }}</Tag>
        </template>
      </template>
      <template #emptyText>
        <Typography.Text type="secondary">点击「刷新」查询(使用当前配置档的目标连接与 DbFlag)</Typography.Text>
      </template>
    </Table>
  </Card>
</template>
