<script setup lang="ts">
import { computed, ref, reactive, onMounted } from 'vue'
import {
  Card, Input, InputPassword, InputNumber, Select, SelectOption,
  Button, Space, message, Alert, Typography
} from 'ant-design-vue'
import { profileStore } from '@/stores/profile'
import { metaStore } from '@/stores/meta'
import { cmdPing, cmdInitDb, sidecarStart } from '@/api/sidecar'
import ConnStringForm from '@/components/ConnStringForm.vue'
import { validateProfile, validateConnString, errorsByField } from '@/utils/validate'

const p = profileStore.profile

// ---- 进入页面：确保 sidecar 就绪、加载元数据与配置档；首次无档时自动创建默认档 ----
const ready = ref(false)
onMounted(async () => {
  try { await sidecarStart() } catch { /* 已运行 */ }
  try {
    await metaStore.load()
    await profileStore.loadAll()
    if (!profileStore.activeName.value) {
      await profileStore.saveAs('默认配置')
    }
  } catch (e) {
    console.warn('加载配置档失败（浏览器调试模式下无 Tauri 后端）:', e)
  }
  ready.value = true
})

// ---- 配置项合法性校验 ----
const fieldErrors = computed(() => errorsByField(validateProfile(p.value)))

// ---- 按钮操作状态：loading + 结果（成功/失败/超时都会落在这里） ----
interface OpState { loading: boolean; result?: { ok: boolean; message: string } }
type OpKey = 'testSource' | 'testDest' | 'initDb'
const btn = reactive<Record<OpKey, OpState>>({
  testSource: { loading: false },
  testDest:   { loading: false },
  initDb:     { loading: false }
})
function setBtnLoading(k: OpKey) { btn[k] = { loading: true, result: undefined } }
function setBtnResult(k: OpKey, r: { ok: boolean; message: string }) { btn[k] = { loading: false, result: r } }
function fmtDur(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)}ms` : `${(ms / 1000).toFixed(2)}s`
}

async function ensureSidecar() {
  await sidecarStart()
}

async function testConn(side: 'source' | 'dest') {
  const key: OpKey = side === 'source' ? 'testSource' : 'testDest'
  const field = side === 'source' ? 'connectionStrings.sourceConn' : 'connectionStrings.destinationConn'
  if (fieldErrors.value[field]) { message.warning(fieldErrors.value[field]); return }

  setBtnLoading(key)
  const t0 = performance.now()
  try {
    await ensureSidecar()
    const connStr = side === 'source'
      ? p.value.connectionStrings.sourceConn
      : p.value.connectionStrings.destinationConn
    const r = await cmdPing(side, connStr)
    setBtnResult(key, { ok: r.ok, message: `${r.message} · ${fmtDur(performance.now() - t0)}` })
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e)
    setBtnResult(key, { ok: false, message: `${msg} · ${fmtDur(performance.now() - t0)}` })
  }
}

async function initDb() {
  const dstErr = fieldErrors.value['connectionStrings.destinationConn']
  if (dstErr) { message.warning(dstErr); return }

  setBtnLoading('initDb')
  const t0 = performance.now()
  try {
    await ensureSidecar()
    const r = await cmdInitDb(p.value)
    setBtnResult('initDb', { ok: r.ok, message: `${r.message} · ${fmtDur(performance.now() - t0)}` })
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e)
    setBtnResult('initDb', { ok: false, message: `${msg} · ${fmtDur(performance.now() - t0)}` })
  }
}

// ---- 手动保存：点击保存按钮后写回当前配置档 ----
const saving = ref(false)

async function saveNow() {
  if (saving.value) return
  const name = profileStore.activeName.value || '默认配置'
  saving.value = true
  try {
    await profileStore.saveAs(name)
    message.success(`配置已保存: ${name}`)
  } catch (e) {
    message.error(`保存失败: ${e instanceof Error ? e.message : String(e)}`)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div>
    <!-- 配置校验摘要 -->
    <Alert
      v-if="Object.keys(fieldErrors).length"
      type="warning"
      show-icon
      :message="`配置有 ${Object.keys(fieldErrors).length} 处待修正`"
      :description="Object.values(fieldErrors).join('；')"
      style="margin-bottom: 12px"
    />

    <!-- 数据库连接：左右两栏，各为紧凑 label:input 布局 -->
    <Card title="数据库连接" size="small" style="margin-bottom: 12px">
      <template #extra>
        <Button size="small" type="primary" :loading="saving" :disabled="!ready" @click="saveNow">保存配置</Button>
      </template>
      <div class="cfg-grid cols-2">
        <div class="cfg-col">
          <ConnStringForm
            :types="metaStore.supported.value.source"
            label="源数据库"
            v-model="p.connectionStrings.sourceConn" />
          <div v-if="validateConnString(p.connectionStrings.sourceConn, 'source')" class="cfg-err">
            {{ validateConnString(p.connectionStrings.sourceConn, 'source') }}
          </div>
          <div class="cfg-actions">
            <Space align="center" wrap>
              <Button size="small" :loading="btn.testSource.loading" :disabled="btn.testSource.loading" @click="testConn('source')">测试源连接</Button>
              <Typography.Text v-if="btn.testSource.result" :type="btn.testSource.result.ok ? 'success' : 'danger'" style="font-size: 11px">
                {{ btn.testSource.result.message }}
              </Typography.Text>
            </Space>
          </div>
        </div>
        <div class="cfg-col">
          <ConnStringForm
            :types="metaStore.supported.value.dest"
            label="目标数据库"
            v-model="p.connectionStrings.destinationConn" />
          <div v-if="validateConnString(p.connectionStrings.destinationConn, 'dest')" class="cfg-err">
            {{ validateConnString(p.connectionStrings.destinationConn, 'dest') }}
          </div>
          <div class="cfg-actions">
            <Space direction="vertical" align="start" size="small" style="width: 100%">
              <Space align="center" wrap>
                <Button size="small" :loading="btn.testDest.loading" :disabled="btn.testDest.loading" @click="testConn('dest')">测试目标连接</Button>
                <Typography.Text v-if="btn.testDest.result" :type="btn.testDest.result.ok ? 'success' : 'danger'" style="font-size: 11px">
                  {{ btn.testDest.result.message }}
                </Typography.Text>
              </Space>
              <Space align="center" wrap>
                <Button size="small" :loading="btn.initDb.loading" :disabled="btn.initDb.loading" @click="initDb">初始化目标库</Button>
                <Typography.Text v-if="btn.initDb.result" :type="btn.initDb.result.ok ? 'success' : 'danger'" style="font-size: 11px">
                  {{ btn.initDb.result.message }}
                </Typography.Text>
              </Space>
            </Space>
          </div>
        </div>
      </div>
    </Card>

    <!-- 上传配置：两栏紧凑布局 -->
    <Card title="上传配置" size="small" style="margin-bottom: 12px">
      <div class="cfg-grid cols-2">
        <div class="cfg-row full">
          <span class="cfg-label">上传地址</span>
          <Input v-model:value="p.upload.url" class="cfg-ctrl" placeholder="http://host/api/Exam/Upload" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">应用ID</span>
          <Input v-model:value="p.upload.jwtAppId" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">服务节点</span>
          <Input v-model:value="p.upload.jwtServerNode" class="cfg-ctrl" placeholder="空则复用应用ID" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">应用密钥</span>
          <InputPassword v-model:value="p.upload.jwtAppSecret" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">令牌有效期</span>
          <InputNumber v-model:value="p.upload.jwtExpiryMinutes" :min="1" class="cfg-ctrl" addon-after="分钟" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">超时时间</span>
          <InputNumber v-model:value="p.upload.timeoutSeconds" :min="5" class="cfg-ctrl" addon-after="秒" />
        </div>
      </div>
    </Card>

    <!-- 迁移参数：两栏紧凑布局 -->
    <Card title="迁移参数" size="small">
      <div class="cfg-grid cols-2">
        <div class="cfg-row">
          <span class="cfg-label">分页大小</span>
          <InputNumber v-model:value="p.migration.pageSize" :min="1" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">并发数</span>
          <InputNumber v-model:value="p.migration.parallelism" :min="1" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">临时表批次</span>
          <InputNumber v-model:value="p.migration.zTempBatchSize" :min="1" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">数据标志</span>
          <Input v-model:value="p.migration.dbFlag" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">计划间隔</span>
          <InputNumber v-model:value="p.migration.planIntervalDays" :min="1" class="cfg-ctrl" addon-after="天" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">最大并发计划</span>
          <InputNumber v-model:value="p.migration.maxParallelPlans" :min="1" class="cfg-ctrl" />
        </div>
        <div class="cfg-row">
          <span class="cfg-label">计划顺序</span>
          <Select v-model:value="p.migration.planOrder" class="cfg-ctrl">
            <SelectOption value="ascending">早 → 晚</SelectOption>
            <SelectOption value="descending">晚 → 早</SelectOption>
          </Select>
        </div>
      </div>
    </Card>
  </div>
</template>

<style scoped>
.cfg-grid {
  display: grid;
  gap: 6px 16px;
}
.cfg-grid.cols-2 {
  grid-template-columns: 1fr 1fr;
}

.cfg-row {
  display: grid;
  grid-template-columns: 84px 1fr;
  align-items: center;
  gap: 6px;
}
.cfg-row.full {
  grid-column: 1 / -1;
}

.cfg-label {
  color: #4e5969;
  font-size: 12px;
  text-align: right;
  padding-right: 4px;
  white-space: nowrap;
  line-height: 1.4;
}

.cfg-ctrl {
  width: 100%;
}

.cfg-col {
  display: flex;
  flex-direction: column;
}

.cfg-actions {
  margin-top: 8px;
  padding-top: 6px;
  border-top: 1px dashed #eef0f3;
}

.cfg-err {
  margin-top: 4px;
  font-size: 11px;
  color: #f5222d;
  line-height: 1.4;
}
</style>
