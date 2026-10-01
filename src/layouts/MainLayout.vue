<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, watch } from 'vue'
import { useRoute, useRouter, type RouteLocationRaw } from 'vue-router'
import { Layout, LayoutContent, Menu, MenuItem, Tag, Modal, Tooltip, notification } from 'ant-design-vue'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { metaStore } from '@/stores/meta'
import { profileStore } from '@/stores/profile'
import { runStore } from '@/stores/run'
import AboutPanel from '@/components/AboutPanel.vue'

const route = useRoute()
const router = useRouter()

const selected = ref<string[]>([route.path])
const showAbout = ref(false)

// 路由变化时同步选中态（配合 v-model:selected-keys 双向绑定，避免 computed 只读触发 antd 内部响应副作用）
watch(
  () => route.path,
  p => { selected.value = [p] },
  { immediate: true }
)

const runStatusColor = computed(() => ({
  idle: 'default', running: 'processing', stopping: 'warning',
  done: 'success', error: 'error'
} as Record<string, string>)[runStore.status.value] ?? 'default')

const runStatusText = computed(() => ({
  idle: '空闲', running: '运行中', stopping: '停止中',
  done: '已完成', error: '出错'
} as Record<string, string>)[runStore.status.value] ?? runStore.status.value)

// ---- 全局运行状态通知：无论用户当前在哪个页面，都能收到迁移启停/完成反馈 ----
watch(
  () => runStore.status.value,
  (newStatus, oldStatus) => {
    // 仅在状态发生实质变化时通知，避免重复
    if (newStatus === oldStatus) return

    if (newStatus === 'running') {
      notification.info({
        message: '迁移已启动',
        description: `模式: ${runStore.label.value || '—'}，可在「迁移执行」页面查看实时进度与日志`,
        placement: 'topRight',
        duration: 6
      })
    } else if (newStatus === 'done') {
      const r = runStore.lastResult.value
      const summary = r?.summary || `成功 ${r?.succeeded ?? 0} / 失败 ${r?.failed ?? 0} / 共 ${r?.total ?? 0}`
      notification.success({
        message: '迁移已完成',
        description: summary,
        placement: 'topRight',
        duration: 8
      })
    } else if (newStatus === 'error') {
      const r = runStore.lastResult.value
      notification.error({
        message: '迁移出错',
        description: r?.message || r?.summary || '请查看「迁移执行」页面日志了解详情',
        placement: 'topRight',
        duration: 10
      })
    } else if (newStatus === 'idle' && (oldStatus === 'done' || oldStatus === 'error' || oldStatus === 'stopping')) {
      notification.info({
        message: '已停止',
        placement: 'topRight',
        duration: 4
      })
    }
  }
)

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
          disabled-overflow
          @click="({ key }) => go(String(key))"
        >
          <MenuItem v-for="m in menus" :key="m.key">{{ m.label }}</MenuItem>
        </Menu>
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

/* 标题栏：跨整个窗口顶部，40px 高；中灰底 + 深字，与冷灰内容区拉开层次 */
.titlebar {
  flex: 0 0 40px;
  height: 40px;
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  padding-right: 148px;   /* 让位给三个自定义按钮 3x46=138 + 10px 缓冲 */
  background: #e5e6eb;
  border-bottom: 1px solid #c9cdd4;
  user-select: none;
}

.tb-left,
.tb-right {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

/* 左上角 logo 图标：可点击，hover 有反馈（蓝底反白） */
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
  background: rgba(0, 0, 0, 0.06);
}

/* 横排菜单贴合 40px 标题栏，浅灰底深字 */
/* min-width 确保 4 个菜单项不会被 antd 的横向折叠误判为溢出（折叠成“...”）*/
.menu-h {
  min-height: 40px;
  min-width: 340px;       /* 4 项 × 每项 ~78px 预留；antd 检测宽 < 项总宽时才折叠 */
  margin: 0;
  padding: 0;
  border-bottom: 0 !important;
  background: transparent !important;
  flex: 0 0 auto;
  flex-shrink: 0;
}
.menu-h :global(.ant-menu-item) {
  padding: 0 14px;
  height: 38px;
  line-height: 38px;
  font-size: 13px;
  color: #4e5969 !important;
}
.menu-h :global(.ant-menu-item:hover) {
  color: #1d2129 !important;
  background: rgba(0, 0, 0, 0.06) !important;
}
/* active/hover/selected 三态的 padding 与 height 必须和普通态完全一致，
   否则 hover 会瞬间改宽度 → 相邻项推移 + vc-overflow ResizeObserver 连锁震荡
   （disabled-overflow 已关掉溢出检测，但 padding 保持一致仍是根本修法） */
.menu-h :global(.ant-menu-item-active),
.menu-h :global(.ant-menu-item-selected) {
  margin:4px 0;
  padding: 0 14px !important;
  color: #1d2129 !important;
  background: #fff !important;
  border-bottom: none !important;
  border-radius: 6px 6px 0 0;
  box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.06);
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
  color: #4e5969;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  outline: none;
}
.wc-btn:hover { background: rgba(0, 0, 0, 0.06); color: #1d2129; }
.wc-btn:active { background: rgba(0, 0, 0, 0.1); color: #1d2129; }
.wc-close:hover { background: #c42b1c; color: #fff; }
.wc-close:active { background: #a02418; color: #fff; }

/* 主布局：填 titlebar 下方剩余空间 */
.main-layout {
  flex: 1;
  overflow: hidden;
  background: transparent;
}

.content {
  padding: 16px;
  overflow: auto;
  background: #eff1f5;
}

/* 全局表单/按钮/卡片样式：拉开 label 与控件的视觉层次 */
.content :global(.ant-card) {
  background: #fff;
  border-radius: 8px;
  border: 1px solid #e6e8eb;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.03);
}
.content :global(.ant-card .ant-card-head) {
  border-bottom: 1px solid #eef0f3;
  min-height: 40px;
}
.content :global(.ant-card .ant-card-head-title) {
  font-size: 14px;
  font-weight: 600;
  color: #1d2129;
  padding: 8px 16px;
}
.content :global(.ant-card .ant-card-body) {
  padding: 16px;
}

/* Label：次要灰 + 加粗，与 input 内黑色值拉开层次 */
.content :global(.ant-form-item .ant-form-item-label > label),
.content :global(.ant-form-item-label > label) {
  color: #4e5969 !important;
  font-weight: 500 !important;
  font-size: 13px !important;
}

/* Input：白底、明显边框，focus 时蓝色高亮 */
.content :global(.ant-input),
.content :global(.ant-input-number-input),
.content :global(.ant-select-selector) {
  color: #1d2129 !important;
  border-color: #c9cdd4 !important;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}
.content :global(.ant-input:hover),
.content :global(.ant-input-number:hover .ant-input-number-input),
.content :global(.ant-select:hover .ant-select-selector) {
  border-color: #1677ff !important;
}
.content :global(.ant-input:focus),
.content :global(.ant-input:focus-visible),
.content :global(.ant-input-number-focused .ant-input-number-input),
.content :global(.ant-select-focused .ant-select-selector) {
  border-color: #1677ff !important;
  box-shadow: 0 0 0 2px rgba(22, 119, 255, 0.16) !important;
}

/* Button：默认按钮白底 + 明显边框 + 灰色文字；primary 按钮强调 */
.content :global(.ant-btn) {
  border-radius: 6px;
  font-weight: 500;
  border-width: 1px;
  border-color: #c9cdd4;
  color: #4e5969;
  transition: color 0.15s ease, border-color 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
}
.content :global(.ant-btn:hover) {
  color: #1677ff;
  border-color: #1677ff;
  background: #f7faff;
}
.content :global(.ant-btn:focus) {
  color: #1677ff;
  border-color: #1677ff;
}
.content :global(.ant-btn-primary) {
  background: #1677ff;
  border-color: #1677ff;
  color: #fff;
  box-shadow: 0 1px 2px rgba(22, 119, 255, 0.3);
}
.content :global(.ant-btn-primary:hover),
.content :global(.ant-btn-primary:focus) {
  background: #4096ff;
  border-color: #4096ff;
  color: #fff;
}
.content :global(.ant-btn-dangerous) {
  border-color: #ff4d4f;
  color: #ff4d4f;
  background: #fff;
}
.content :global(.ant-btn-dangerous:hover) {
  border-color: #ff7875;
  color: #ff7875;
  background: #fff5f5;
}
</style>
