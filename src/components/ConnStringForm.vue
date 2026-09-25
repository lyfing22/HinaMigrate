<script setup lang="ts">
import { reactive } from 'vue'
import { Form, FormItem, Input, InputPassword, Select, SelectOption, Typography, Divider } from 'ant-design-vue'
import { parseConnString, buildConnString, type ConnFields } from '@/api/connString'

const props = defineProps<{ modelValue: string; types: string[]; label: string }>()
const emit = defineEmits<{ 'update:modelValue': [string] }>()

// 初始化一次(父组件用 key 在切换配置档时强制重建)
const f = reactive<ConnFields>(parseConnString(props.modelValue))

function sync() {
  emit('update:modelValue', buildConnString(f))
}
</script>

<template>
  <div>
    <Typography.Title :level="5">{{ label }}</Typography.Title>
    <Form layout="vertical" :model="f">
      <FormItem label="数据库类型">
        <Select v-model:value="f.dbType" @change="sync" placeholder="选择数据库类型">
          <SelectOption v-for="t in types" :key="t" :value="t">{{ t }}</SelectOption>
        </Select>
      </FormItem>
      <div class="row">
        <FormItem label="Host / Server" class="flex-1">
          <Input v-model:value="f.host" @input="sync" placeholder="192.168.1.165" />
        </FormItem>
        <FormItem label="Port" style="width: 140px">
          <Input v-model:value="f.port" @input="sync" placeholder="54321" />
        </FormItem>
      </div>
      <FormItem label="Database">
        <Input v-model:value="f.database" @input="sync" />
      </FormItem>
      <div class="row">
        <FormItem label="User" class="flex-1">
          <Input v-model:value="f.user" @input="sync" />
        </FormItem>
        <FormItem label="Password" style="width: 240px">
          <InputPassword v-model:value="f.password" @input="sync" />
        </FormItem>
      </div>
      <FormItem label="额外参数(原样透传,如 TrustServerCertificate=true;serverSelectionTimeoutMS=5000)">
        <Input v-model:value="f.extra" @input="sync" />
      </FormItem>
      <Divider style="margin: 8px 0" />
      <Typography.Text type="secondary">预览: {{ modelValue }}</Typography.Text>
    </Form>
  </div>
</template>

<style scoped>
.row { display: flex; gap: 12px; align-items: flex-start; }
</style>
