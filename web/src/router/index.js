import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { title: '登录', public: true }
  },
  {
    path: '/register',
    name: 'register',
    component: () => import('@/views/RegisterView.vue'),
    meta: { title: '注册', public: true }
  },
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    children: [
      {
        path: '',
        name: 'dashboard',
        component: () => import('@/views/DashboardView.vue'),
        meta: { title: '仪表盘', tagline: '账户资产与策略运行总览' }
      },
      {
        path: 'market',
        name: 'market',
        component: () => import('@/views/MarketView.vue'),
        meta: { title: '行情', tagline: '实时 K 线、24 小时行情与历史数据同步' }
      },
      {
        path: 'strategies',
        name: 'strategies',
        component: () => import('@/views/StrategiesView.vue'),
        meta: { title: '策略库', tagline: '内置策略、参数说明与快速入口' }
      },
      {
        path: 'ai',
        name: 'ai',
        component: () => import('@/views/AiView.vue'),
        meta: { title: 'AI 决策', tagline: '走势分析、策略生成与一键回测' }
      },
      {
        path: 'backtest',
        name: 'backtest',
        component: () => import('@/views/BacktestView.vue'),
        meta: { title: '策略回测', tagline: '历史行情回测、绩效指标与交易明细' }
      },
      {
        path: 'trade',
        name: 'trade',
        component: () => import('@/views/TradeView.vue'),
        meta: { title: '交易台', tagline: '手动下单、杠杆与策略任务交易' }
      },
      {
        path: 'tasks',
        name: 'tasks',
        component: () => import('@/views/TasksView.vue'),
        meta: { title: '交易任务', tagline: '自动交易任务与风控参数管理' }
      },
      {
        path: 'orders',
        name: 'orders',
        component: () => import('@/views/OrdersView.vue'),
        meta: { title: '订单与持仓', tagline: '成交记录、当前持仓与浮动盈亏' }
      },
      {
        path: 'profile',
        name: 'profile',
        component: () => import('@/views/ProfileView.vue'),
        meta: { title: '个人中心', tagline: '账号资料、安全设置与多账号切换' }
      },
      {
        path: 'settings',
        name: 'settings',
        component: () => import('@/views/SettingsView.vue'),
        meta: { title: '系统设置', tagline: 'OKX API 接入、真实账户与系统状态' }
      }
    ]
  },
  { path: '/:pathMatch(.*)*', redirect: '/' }
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 })
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.public) {
    const adding = to.query?.add === '1'
    if (auth.isAuthenticated && !adding && (to.path === '/login' || to.path === '/register')) {
      return { path: '/' }
    }
    return true
  }

  if (!auth.isAuthenticated) {
    return { path: '/login', query: to.fullPath && to.fullPath !== '/' ? { redirect: to.fullPath } : {} }
  }

  if (!auth.user) {
    try {
      await auth.fetchMe()
    } catch (err) {
      if (err?.status === 401) {
        auth.logout()
        return { path: '/login', query: { redirect: to.fullPath } }
      }
      // 后端暂不可达时仍允许进入页面，由页面展示错误态
    }
  }

  return true
})

router.afterEach((to) => {
  const title = to.meta?.title
  document.title = title ? `${title} · QTS 量化交易系统` : 'QTS · 量化交易系统'
})

export default router
