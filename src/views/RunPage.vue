<script setup lang="ts">
import { computed } from 'vue'
import {
  Card, RadioGroup, RadioButton, Input, Button, Space, message, Row, Col, Typography, Alert
} from 'ant-design-vue'
import { runStore } from '@/stores/run'
import { profileStore } from '@/stores/profile'
import ProgressPanel from '@/components/ProgressPanel.vue'
import LogPanel from '@/components/LogPanel.vue'

const p = profileStore.profile
const isRunning = computed(() => runStore.status.value === 'running' || runStore.status.value === 'stopping')

async function start() {
  if (!p.value.connectionStrings.sourceConn || !p.value.connectionStrings.destinationConn) {
    message.warning('请先在配置页填写源/目标连接信息'); return
  }
  if (p.value.migration.mode === 'batch' && (!p.value.migration.timeRange.start || !p.value.migration.timeRange.end)) {
    message.warning('batch 模式需配置时间范围'); return
  }
  if (p.value.migration.mode === 'debug' && !p.value.migration.examId) {
    message.warning('debug 模式需填写 ExamId'); return
  }
  try {
    await runStore.start(p.value)
    message.success('已启动')
  } catch (e) {
    message.error(`启动失败: ${(e as Error).message}`)
  }
}

async function stop() {
  await runStore.stop()
}
</script>

<template>
  <div>
    <Card title="迁移执行" size="small">
      <Space direction="vertical" style="width: 100%">
        <div>
          <Typography.Text strong>运行模式：</Typography.Text>
          <RadioGroup v-model:value="p.migration.mode" button-style="solid" :disabled="isRunning">
            <RadioButton value="batch">batch 全量</RadioButton>
            <RadioButton value="debug">debug 单条</RadioButton>
            <RadioButton value="retry">retry 重试</RadioButton>
          </RadioGroup>
        </div>

        <div v-if="p.migration.mode === 'batch'" style="display: flex; gap: 12px; align-items: center">
          <Typography.Text>时间范围：</Typography.Text>
          <Input v-model:value="p.migration.timeRange.start" placeholder="2020-01-01" style="width: 180px" />
          <Typography.Text>~</Typography.Text>
          <Input v-model:value="p.migration.timeRange.end" placeholder="2024-12-31" style="width: 180px" />
        </div>

        <div v-if="p.migration.mode === 'debug'" style="display: flex; gap: 12px; align-items: center">
          <Typography.Text>ExamId：</Typography.Text>
          <Input v-model:value="p.migration.examId" placeholder="OrgCode|ExamId" style="width: 320px" />
        </div>

        <Alert v-if="p.migration.mode === 'retry'" type="info" show-icon message="retry 模式：从目标库 ZTemp_MigrateError 读取全部失败记录并重试(成功后清除错误行)。" />

        <Space>
          <Button type="primary" :loading="isRunning" @click="start" :disabled="isRunning">启动</Button>
          <Button danger :disabled="!isRunning" @click="stop">停止</Button>
        </Space>
      </Space>
    </Card>

    <Row :gutter="16" style="margin-top: 16px">
      <Col :span="10">
        <Card title="进度" size="small" style="height: 100%">
          <ProgressPanel :progress="runStore.progress.value" :status="runStore.status.value" />
        </Card>
      </Col>
      <Col :span="14">
        <Card title="日志" size="small" :body-style="{ height: '460px', padding: '8px' }">
          <LogPanel :logs="runStore.logs.value" :status="runStore.status.value" />
        </Card>
      </Col>
    </Row>
  </div>
</template>
