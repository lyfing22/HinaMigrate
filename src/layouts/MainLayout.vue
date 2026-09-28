<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref } from 'vue'
import { useRoute, useRouter, type RouteLocationRaw } from 'vue-router'
import { Layout, LayoutContent, Menu, MenuItem, Tag, Modal, Tooltip } from 'ant-design-vue'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { metaStore } from '@/stores/meta'
import { profileStore } from '@/stores/profile'
import { runStore } from '@/stores/run'
import AboutPanel from '@/components/AboutPanel.vue'

const route = useRoute()
const router = useRouter()

const selected = computed(() => [route.path])
const showAbout = ref(false)

const runStatusColor = computed(() => ({
  idle: 'default', running: 'processing', stopping: 'warning',
  done: 'success', error: 'error'
} as Record<string, string>)[runStore.status.value] ?? 'default')

const runStatusText = computed(() => ({
  idle: '空闲', running: '运行中', stopping: '停止中',
  done: '已完成', error: '出错'
} as Record<string, string>)[runStore.status.value] ?? runStore.status.value)

const menus = [
  { key: '/config', label: '配置管理' },
  { key: '/run', label: '迁移执行' },
  { key: '/plans', label: '计划追踪' },
  { key: '/errors', label: '错误重试' }
]
function go(path: string) { router.push(path as RouteLocationRaw) }

// ---- 窗口控制（frameless 场景） ----
// 浏览器（pnpm dev 调 UI）无 __TAURI_INTERNALS__，此时隐藏窗口三键并跳过窗口 API
const isTauri = '__TAURI_INTERNALS__' in window
const win = isTauri ? getCurrentWindow() : null
const isMaximized = ref(false)

async function refreshMaxState() {
  if (!win) return
  isMaximized.value = await win.isMaximized()
}

function minimize() { win?.minimize() }
function toggleMax() { win?.toggleMaximize() }
function close() { win?.close() }

let unlisten: (() => void) | null = null
onMounted(async () => {
  await metaStore.load()
  try { await profileStore.loadAll() } catch { /* 浏览器/sidecar 未就绪时忽略 */ }
  if (!win) return
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
        <!-- 左上角 logo：hover 显示"福建影像迁移"，点击弹出"关于" -->
        <Tooltip title="福建影像迁移" placement="bottomLeft">
          <img
            class="logo-img"
            src="/logo.png"
            alt=""
            data-tauri-drag-region="false"
            @click="showAbout = true"
          />
        </Tooltip>

        <!-- 横排操作菜单 -->
        <Menu
          v-model:selected-keys="selected"
          mode="horizontal"
          theme="light"
          class="menu-h"
          data-tauri-drag-region="false"
          @click="({ key }) => go(String(key))"
        >
          <MenuItem v-for="m in menus" :key="m.key">{{ m.label }}</MenuItem>
        </Menu>

        <Tag v-if="profileStore.hasActive.value" color="blue" data-tauri-drag-region="false">{{ profileStore.activeName.value }}</Tag>
        <Tag v-if="profileStore.dirty.value" color="orange" data-tauri-drag-region="false">未保存</Tag>
      </div>
      <div class="tb-right">
        <Tag :color="runStatusColor" data-tauri-drag-region="false">{{ runStatusText }}</Tag>
      </div>

      <!-- 自定义窗口三键（绝对定位到 titlebar 右上；浏览器调试时隐藏） -->
      <div v-if="isTauri" class="wc" data-tauri-drag-region="false">
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

    <!-- 内容区填满标题栏下方 -->
    <Layout class="main-layout">
      <LayoutContent class="content">
        <router-view />
      </LayoutContent>
    </Layout>

    <!-- 关于弹层 -->
    <Modal v-model:open="showAbout" title="关于" :footer="null" width="640px">
      <AboutPanel />
    </Modal>
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
  gap: 12px;
  min-width: 0;
}

/* 左上角 logo 图标：可点击，hover 有反馈 */
.logo-img {
  width: 22px;
  height: 22px;
  display: block;
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  transition: background 0.15s ease;
  user-select: none;
}
.logo-img:hover {
  background: rgba(22, 119, 255, 0.12);
}

/* 横排菜单贴合 40px 标题栏 */
.menu-h {
  min-height: 40px;
  margin: 0;
  padding: 0;
  border-bottom: 0 !important;
  background: transparent !important;
  flex: 0 0 auto;
}
.menu-h :global(.ant-menu-item) {
  padding: 0 14px;
  height: 38px;
  line-height: 38px;
  font-size: 13px;
}
.menu-h :global(.ant-menu-item-active),
.menu-h :global(.ant-menu-item-selected) {
  height: 38px;
  line-height: 38px;
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

.content {
  padding: 16px;
  overflow: auto;
  background: rgba(243, 243, 243, 0.92);
}
</style>
