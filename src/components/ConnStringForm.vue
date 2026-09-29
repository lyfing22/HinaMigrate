<script setup lang="ts">
import { reactive, watch, nextTick } from 'vue'
import { Input, InputPassword, Select, SelectOption } from 'ant-design-vue'
import { parseConnString, buildConnString, type ConnFields } from '@/api/connString'

const props = defineProps<{ modelValue: string; types: string[]; label: string }>()
const emit = defineEmits<{ 'update:modelValue': [string] }>()

// 内部字段状态；所有变更统一通过 watch 同步到 modelValue
const f = reactive<ConnFields>(parseConnString(props.modelValue))

// 标志位：外部更新 modelValue → 解析回 f 时，抑制 f 的 watch 避免死循环
let externalUpdate = false

// f 变更 → 同步到父组件 modelValue（用户输入触发）
watch(f, () => {
  if (externalUpdate) return
  emit('update:modelValue', buildConnString(f))
}, { deep: true })

// 父组件外部更新 modelValue（如 loadAll 加载已保存配置档）→ 重新解析到 f
watch(
  () => props.modelValue,
  (next) => {
    if (buildConnString(f) !== next) {
      externalUpdate = true
      Object.assign(f, parseConnString(next))
      nextTick(() => { externalUpdate = false })
    }
  }
)
</script>

<template>
  <div class="cfg-form">
    <div class="cfg-title">{{ label }}</div>

    <div class="cfg-row">
      <span class="cfg-label">数据库类型</span>
      <Select v-model:value="f.dbType" class="cfg-ctrl" placeholder="选择">
        <SelectOption v-for="t in types" :key="t" :value="t">{{ t }}</SelectOption>
      </Select>
    </div>
    <div class="cfg-row">
      <span class="cfg-label">主机</span>
      <Input v-model:value="f.host" class="cfg-ctrl" placeholder="192.168.1.165" />
    </div>
    <div class="cfg-row">
      <span class="cfg-label">端口</span>
      <Input v-model:value="f.port" class="cfg-ctrl" placeholder="27017" />
    </div>
    <div class="cfg-row">
      <span class="cfg-label">数据库</span>
      <Input v-model:value="f.database" class="cfg-ctrl" />
    </div>
    <div class="cfg-row">
      <span class="cfg-label">用户名</span>
      <Input v-model:value="f.user" class="cfg-ctrl" />
    </div>
    <div class="cfg-row">
      <span class="cfg-label">密码</span>
      <InputPassword v-model:value="f.password" class="cfg-ctrl" />
    </div>
    <div class="cfg-row">
      <span class="cfg-label">额外参数</span>
      <Input v-model:value="f.extra" class="cfg-ctrl" />
    </div>

    <div class="cfg-preview">
      <span class="cfg-label">预览</span>
      <span class="cfg-preview-text">{{ modelValue || '—' }}</span>
    </div>
  </div>
</template>

<style scoped>
.cfg-form { font-size: 12px; }

.cfg-title {
  font-size: 13px;
  font-weight: 600;
  color: #1d2129;
  margin-bottom: 8px;
  padding-bottom: 5px;
  border-bottom: 1px solid #eef0f3;
}

.cfg-row {
  display: grid;
  grid-template-columns: 64px 1fr;
  align-items: center;
  gap: 6px;
  margin-bottom: 5px;
}
.cfg-row:last-of-type { margin-bottom: 0; }

.cfg-label {
  color: #4e5969;
  font-size: 12px;
  text-align: right;
  padding-right: 4px;
  white-space: nowrap;
  line-height: 1.4;
}

.cfg-ctrl { width: 100%; }

.cfg-preview {
  margin-top: 8px;
  padding-top: 6px;
  border-top: 1px dashed #eef0f3;
  display: grid;
  grid-template-columns: 64px 1fr;
  gap: 6px;
  align-items: start;
}
.cfg-preview-text {
  font-size: 11px;
  color: #86909c;
  word-break: break-all;
  line-height: 1.5;
  font-family: 'Consolas', 'Courier New', monospace;
}
</style>
