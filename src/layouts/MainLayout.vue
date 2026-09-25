<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useRoute, useRouter, type RouteLocationRaw } from 'vue-router'
import { Layout, LayoutSider, LayoutHeader, LayoutContent, Menu, MenuItem, Tag, Space, Typography } from 'ant-design-vue'
import { metaStore } from '@/stores/meta'
import { profileStore } from '@/stores/profile'
import { runStore } from '@/stores/run'

const route = useRoute()
const router = useRouter()

const selected = computed(() => [route.path])
const runStatusColor = computed(() => ({
  idle: 'default', running: 'processing', stopping: 'warning',
  done: 'success', error: 'error'
} as Record<string, string>)[runStore.status.value] ?? 'default')
const runStatusText = computed(() => ({
  idle: '空闲', running: '运行中', stopping: '停止中',
  done: '已完成', error: '出错'
} as Record<string, string>)[runStore.status.value] ?? runStore.status.value)

const menus = [
  { key: '/run', label: '迁移执行' },
  { key: '/config', label: '配置管理' },
  { key: '/plans', label: '计划追踪' },
  { key: '/errors', label: '错误重试' },
  { key: '/about', label: '关于' }
]
function go(path: string) { router.push(path as RouteLocationRaw) }

onMounted(async () => {
  await metaStore.load()
  await profileStore.loadAll()
})
</script>

<template>
  <Layout style="height: 100vh">
    <LayoutSider :width="180" theme="light" style="border-right: 1px solid #f0f0f0">
      <div class="logo">福建影像迁移</div>
      <Menu v-model:selected-keys="selected" mode="inline" theme="light" style="border: 0">
        <MenuItem v-for="m in menus" :key="m.key" @click="go(m.key)">{{ m.label }}</MenuItem>
      </Menu>
    </LayoutSider>
    <Layout>
      <LayoutHeader class="header">
        <Space>
          <Typography.Text strong>当前配置档:</Typography.Text>
          <Tag v-if="profileStore.hasActive.value" color="blue">{{ profileStore.activeName.value }}</Tag>
          <Tag v-else>(未保存)</Tag>
          <Tag v-if="profileStore.dirty.value" color="orange">未保存</Tag>
        </Space>
        <Space>
          <Typography.Text>运行状态:</Typography.Text>
          <Tag :color="runStatusColor">{{ runStatusText }}</Tag>
        </Space>
      </LayoutHeader>
      <LayoutContent class="content">
        <router-view />
      </LayoutContent>
    </Layout>
  </Layout>
</template>

<style scoped>
.logo {
  height: 56px;
  line-height: 56px;
  text-align: center;
  font-weight: 600;
  color: #1677ff;
  border-bottom: 1px solid #f0f0f0;
}
.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background: #fff;
  border-bottom: 1px solid #f0f0f0;
  padding: 0 20px;
  height: 56px;
  line-height: 56px;
}
.content {
  padding: 16px;
  overflow: auto;
  background: #f0f2f5;
}
</style>
