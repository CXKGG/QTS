<template>
  <aside class="app-sidebar">
    <div class="sidebar-brand">
      <div class="brand-mark">
        <el-icon><DataLine /></el-icon>
      </div>
      <div class="brand-text">
        <span class="brand-name">QTS</span>
        <span class="brand-sub">量化交易系统</span>
      </div>
    </div>

    <nav class="sidebar-nav">
      <template v-for="section in navSections" :key="section.label">
        <div class="nav-group-label">{{ section.label }}</div>
        <router-link
          v-for="item in section.items"
          :key="item.path"
          :to="item.path"
          class="nav-item"
          :class="{ active: isActive(item.path) }"
          :title="collapsed ? item.label : item.label"
          @click="$emit('navigate')"
        >
          <el-icon><component :is="item.icon" /></el-icon>
          <span class="nav-label">{{ item.label }}</span>
        </router-link>
      </template>
    </nav>

    <div class="sidebar-footer">
      <div class="risk-notice">
        <strong><el-icon><WarningFilled /></el-icon>风险提示</strong>
        交易存在本金损失风险；请确认设置页的模拟/实盘环境后谨慎操作。
      </div>
      <div class="sidebar-meta">
        <span>QTS v1.0.0</span>
        <span>OKX · USDT</span>
      </div>
      <button type="button" class="sidebar-collapse-btn" @click="$emit('toggle')">
        <el-icon><component :is="collapsed ? 'Expand' : 'Fold'" /></el-icon>
        <span>{{ collapsed ? '展开菜单' : '收起菜单' }}</span>
      </button>
    </div>
  </aside>
</template>

<script setup>
import { useRoute } from 'vue-router'

defineProps({
  collapsed: { type: Boolean, default: false }
})
defineEmits(['toggle', 'navigate'])

const route = useRoute()

const navSections = [
  {
    label: '监控',
    items: [
      { path: '/', label: '仪表盘', icon: 'Odometer' },
      { path: '/market', label: '行情', icon: 'TrendCharts' }
    ]
  },
  {
    label: '策略交易',
    items: [
      { path: '/strategies', label: '策略库', icon: 'Cpu' },
      { path: '/ai', label: 'AI 决策', icon: 'MagicStick' },
      { path: '/backtest', label: '策略回测', icon: 'DataAnalysis' },
      { path: '/trade', label: '交易台', icon: 'Switch' },
      { path: '/tasks', label: '交易任务', icon: 'AlarmClock' },
      { path: '/orders', label: '订单与持仓', icon: 'Wallet' }
    ]
  },
  {
    label: '系统',
    items: [
      { path: '/profile', label: '个人中心', icon: 'User' },
      { path: '/settings', label: '系统设置', icon: 'Setting' }
    ]
  }
]

function isActive(path) {
  if (path === '/') return route.path === '/'
  return route.path === path || route.path.startsWith(`${path}/`)
}
</script>

<style scoped>
@media (max-width: 1024px) {
  .sidebar-collapse-btn {
    display: none;
  }
}
</style>
