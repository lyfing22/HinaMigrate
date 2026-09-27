<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref } from 'vue'
import { useRoute, useRouter, type RouteLocationRaw } from 'vue-router'
import { Layout, LayoutSider, LayoutContent, Menu, MenuItem, Tag } from 'ant-design-vue'
import { getCurrentWindow } from '@tauri-apps/api/window'
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

const currentLabel = computed(() => menus.find(m => m.key === route.path)?.label ?? '')

// ---- 窗口控制（frameless 场景） ----
const win = getCurrentWindow()
const isMaximized = ref(false)

async function refreshMaxState() {
  isMaximized.value = await win.isMaximized()
}

function minimize() { win.minimize() }
function toggleMax() { win.toggleMaximize() }
function close() { win.close() }

let unlisten: (() => void) | null = null
onMounted(async () => {
  await metaStore.load()
  await profileStore.loadAll()
  await refreshMaxState()
  unlisten = await win.onResized(async () => {
    await refreshMaxState()
  })
})
onUnmounted(() => { unlisten?.() })
</script>

<template>
  <div class="app-shell">
    <!-- 顶部跨整个窗口的标题栏（Explorer 风格） -->
    <div class="titlebar" data-tauri-drag-region>
      <div class="tb-left">
        <span class="logo-text" data-tauri-drag-region>福建影像迁移</span>
        <span class="tb-sep" data-tauri-drag-region>/</span>
        <span class="crumb" data-tauri-drag-region>{{ currentLabel }}</span>
        <Tag v-if="profileStore.hasActive.value" color="blue" data-tauri-drag-region="false">{{ profileStore.activeName.value }}</Tag>
        <Tag v-if="profileStore.dirty.value" color="orange" data-tauri-drag-region="false">未保存</Tag>
      </div>
      <div class="tb-right">
        <Tag :color="runStatusColor" data-tauri-drag-region="false">{{ runStatusText }}</Tag>
      </div>

      <!-- 自定义窗口三键（绝对定位到 titlebar 右上） -->
      <div class="wc" data-tauri-drag-region="false">
        <button class="wc-btn" title="最小化" @click="minimize">
          <svg width="10" height="1" viewBox="0 0 10 1"><rect width="10" height="1" fill="currentColor"/></svg>
        </button>
        <button class="wc-btn" :title="isMaximized ? '还原' : '最大化'" @click="toggleMax">
          <svg v-if="!isMaximized" width="10" height="10" viewBox="0 0 10 10">
            <rect width="10" height="10" fill="none" stroke="currentColor" stroke-width="1"/>
          </svg>
          <svg v-else width="10" height="10" viewBox="0 0 10 10">
            <path d="M2 2 H8 V8 H2 Z" fill="none" stroke="currentColor" stroke-width="1"/>
            <path d="M2 2 H8 V6" fill="none" stroke="currentColor" stroke-width="1" opacity="0.4"/>
          </svg>
        </button>
        <button class="wc-btn wc-close" title="关闭" @click="close">
          <svg width="10" height="10" viewBox="0 0 10 10">
            <path d="M0 0 L10 10 M10 0 L0 10" stroke="currentColor" stroke-width="1"/>
          </svg>
        </button>
      </div>
    </div>

    <!-- 下面才是 Sider + Content -->
    <Layout class="main-layout">
      <LayoutSider width="220" theme="light" class="sider">
        <Menu v-model:selected-keys="selected" mode="inline" theme="light" class="menu">
          <MenuItem v-for="m in menus" :key="m.key" @click="go(m.key)">{{ m.label }}</MenuItem>
        </Menu>
      </LayoutSider>
      <LayoutContent class="content">
        <router-view />
      </LayoutContent>
    </Layout>
  </div>
</template>

<style scoped>
/* 全透明窗口 + Mica 效果，兜底底色避免 Win10 看到桌面 */
.app-shell {
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: transparent;
}

:global(body),
:global(html) {
  background: transparent;
  margin: 0;
  padding: 0;
}

/* 标题栏：跨整个窗口顶部，40px 高 */
.titlebar {
  flex: 0 0 40px;
  height: 40px;
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  padding-right: 148px;   /* 让位给三个自定义按钮 3x46=138 + 10px 缓冲 */
  background: rgba(250, 250, 250, 0.92);
  border-bottom: 1px solid #e5e5e5;
  user-select: none;
}

.tb-left,
.tb-right {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.logo-text {
  font-weight: 600;
  color: #1677ff;
  user-select: none;
  font-size: 13px;
  white-space: nowrap;
}

.tb-sep {
  color: #999;
  user-select: none;
  font-size: 13px;
}

.crumb {
  font-weight: 500;
  color: #333;
  user-select: none;
  font-size: 13px;
}

/* 自定义窗口三键 */
.wc {
  position: absolute;
  top: 0;
  right: 0;
  height: 100%;
  display: flex;
  user-select: none;
}
.wc-btn {
  width: 46px;
  height: 100%;
  border: none;
  background: transparent;
  color: #333;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  outline: none;
}
.wc-btn:hover { background: rgba(0, 0, 0, 0.06); }
.wc-btn:active { background: rgba(0, 0, 0, 0.1); }
.wc-close:hover { background: #c42b1c; color: #fff; }

/* 主布局：填 titlebar 下方剩余空间 */
.main-layout {
  flex: 1;
  overflow: hidden;
  background: transparent;
}

.sider {
  background: rgba(250, 250, 250, 0.92);
  border-right: 1px solid #e5e5e5;
}

.menu {
  border: 0;
}

.content {
  padding: 16px;
  overflow: auto;
  background: rgba(243, 243, 243, 0.92);
}
</style>
