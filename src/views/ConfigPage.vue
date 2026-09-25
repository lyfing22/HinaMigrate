<script setup lang="ts">
import { ref } from 'vue'
import {
  Card, Form, FormItem, Input, InputPassword, InputNumber, Select, SelectOption,
  Button, Space, message, Row, Col
} from 'ant-design-vue'
import { profileStore } from '@/stores/profile'
import { metaStore } from '@/stores/meta'
import { cmdPing, cmdInitDb, sidecarStart } from '@/api/sidecar'
import ConnStringForm from '@/components/ConnStringForm.vue'

const p = profileStore.profile
const newName = ref('')

async function ensureSidecar() {
  try { await sidecarStart() } catch { /* 已运行 */ }
}

async function testConn(side: 'source' | 'dest') {
  const connStr = side === 'source' ? p.value.connectionStrings.sourceConn : p.value.connectionStrings.destinationConn
  if (!connStr) { message.warning('请先填写连接信息'); return }
  await ensureSidecar()
  const r = await cmdPing(side, connStr)
  r.ok ? message.success(r.message) : message.error(r.message)
}

async function initDb() {
  if (!p.value.connectionStrings.destinationConn) { message.warning('请先填写目标连接信息'); return }
  await ensureSidecar()
  const r = await cmdInitDb(p.value)
  r.ok ? message.success(r.message) : message.error(r.message)
}

async function save() {
  const name = newName.value.trim() || profileStore.activeName.value
  if (!name) { message.warning('请输入配置档名称'); return }
  await profileStore.saveAs(name)
  newName.value = ''
  message.success(`已保存配置档: ${name}`)
}

async function remove() {
  if (!profileStore.activeName.value) return
  await profileStore.remove(profileStore.activeName.value)
  message.success('已删除')
}
</script>

<template>
  <div>
    <Card title="配置档" size="small" style="margin-bottom: 16px">
      <Space wrap>
        <Select :value="profileStore.activeName.value" style="width: 200px" placeholder="选择配置档"
          @change="(v: unknown) => profileStore.activate(String(v))">
          <SelectOption v-for="m in profileStore.list.value" :key="m.name" :value="m.name">{{ m.name }}</SelectOption>
        </Select>
        <Button @click="profileStore.newProfile()">新建</Button>
        <Input v-model:value="newName" placeholder="另存为名称" style="width: 180px" />
        <Button type="primary" @click="save">保存</Button>
        <Button danger :disabled="!profileStore.hasActive.value" @click="remove">删除</Button>
      </Space>
    </Card>

    <Row :gutter="16">
      <Col :span="12">
        <Card title="源端连接" size="small">
          <ConnStringForm
            :key="'src-' + profileStore.activeName"
            :types="metaStore.supported.value.source"
            label="源数据库"
            v-model="p.connectionStrings.sourceConn" />
          <Button style="margin-top: 8px" @click="testConn('source')">测试源连接</Button>
        </Card>
      </Col>
      <Col :span="12">
        <Card title="目标端连接" size="small">
          <ConnStringForm
            :key="'dst-' + profileStore.activeName"
            :types="metaStore.supported.value.dest"
            label="目标数据库"
            v-model="p.connectionStrings.destinationConn" />
          <Button style="margin-top: 8px" @click="testConn('dest')">测试目标连接</Button>
          <Button style="margin-left: 8px" @click="initDb">初始化目标库</Button>
        </Card>
      </Col>
    </Row>

    <Card title="上传与 JWT" size="small" style="margin-top: 16px">
      <Form layout="vertical">
        <FormItem label="上传 URL"><Input v-model:value="p.upload.url" /></FormItem>
        <Row :gutter="16">
          <Col :span="8"><FormItem label="JwtAppId"><Input v-model:value="p.upload.jwtAppId" /></FormItem></Col>
          <Col :span="8"><FormItem label="JwtServerNode(空则复用 AppId)"><Input v-model:value="p.upload.jwtServerNode" /></FormItem></Col>
          <Col :span="8"><FormItem label="JwtAppSecret"><InputPassword v-model:value="p.upload.jwtAppSecret" /></FormItem></Col>
        </Row>
        <Row :gutter="16">
          <Col :span="12"><FormItem label="JwtExpiryMinutes"><InputNumber v-model:value="p.upload.jwtExpiryMinutes" :min="1" style="width: 100%" /></FormItem></Col>
          <Col :span="12"><FormItem label="TimeoutSeconds"><InputNumber v-model:value="p.upload.timeoutSeconds" :min="5" style="width: 100%" /></FormItem></Col>
        </Row>
      </Form>
    </Card>

    <Card title="迁移参数" size="small" style="margin-top: 16px">
      <Form layout="vertical">
        <Row :gutter="16">
          <Col :span="6"><FormItem label="PageSize"><InputNumber v-model:value="p.migration.pageSize" :min="1" style="width: 100%" /></FormItem></Col>
          <Col :span="6"><FormItem label="Parallelism"><InputNumber v-model:value="p.migration.parallelism" :min="1" style="width: 100%" /></FormItem></Col>
          <Col :span="6"><FormItem label="ZTempBatchSize"><InputNumber v-model:value="p.migration.zTempBatchSize" :min="1" style="width: 100%" /></FormItem></Col>
          <Col :span="6"><FormItem label="DbFlag"><Input v-model:value="p.migration.dbFlag" /></FormItem></Col>
        </Row>
        <Row :gutter="16">
          <Col :span="8"><FormItem label="PlanIntervalDays"><InputNumber v-model:value="p.migration.planIntervalDays" :min="1" style="width: 100%" /></FormItem></Col>
          <Col :span="8"><FormItem label="MaxParallelPlans"><InputNumber v-model:value="p.migration.maxParallelPlans" :min="1" style="width: 100%" /></FormItem></Col>
          <Col :span="8">
            <FormItem label="PlanOrder">
              <Select v-model:value="p.migration.planOrder">
                <SelectOption value="ascending">ascending(早→晚)</SelectOption>
                <SelectOption value="descending">descending(晚→早)</SelectOption>
              </Select>
            </FormItem>
          </Col>
        </Row>
      </Form>
    </Card>
  </div>
</template>
