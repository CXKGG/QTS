<template>
  <div class="page-stack">
    <PageHeader title="仪表盘" subtitle="OKX API 账户、策略运行与目标进度总览">
      <template #tag>
        <el-tag v-if="lastUpdated" size="small" type="info" effect="plain">更新于 {{ lastUpdated }}</el-tag>
      </template>
      <el-button :icon="Refresh" :loading="loading" @click="refreshAll">刷新</el-button>
      <el-button :icon="DocumentAdd" type="primary" @click="go('/backtest')">新建回测</el-button>
    </PageHeader>

    <el-alert v-if="error" type="error" :title="error" show-icon :closable="false" />

    <div v-if="loading && !data" class="qts-card dash-skeleton">
      <el-skeleton :rows="10" animated />
    </div>

    <template v-else-if="data">
      <div class="stat-grid">
        <StatCard
          :label="useLiveAccount ? 'OKX API 总权益' : '纸面总资产'"
          :value="displayEquity"
          unit="USDT"
          icon="Wallet"
          accent="cyan"
          :trend="useLiveAccount ? 0 : paper.returnPct"
          :sub="useLiveAccount ? '已连接 OKX API 账户' : `初始资金 ${fmtMoney(paper.initialBalance)} USDT`"
          :spark="useLiveAccount ? [] : curveSpark"
        />
        <StatCard
          label="可用 USDT"
          :value="displayAvailable"
          unit="USDT"
          icon="Coin"
          accent="blue"
          :sub="useLiveAccount ? '来自 OKX API 账户' : `持仓市值 ${fmtMoney(paper.positionValue)} USDT`"
        />
        <StatCard
          :label="useLiveAccount ? '浮动盈亏' : '累计盈亏'"
          :value="useLiveAccount ? displayUnrealized : paper.realizedPnl"
          unit="USDT"
          icon="TrendCharts"
          :accent="pnlAccent(useLiveAccount ? displayUnrealized : paper.realizedPnl)"
          :trend="useLiveAccount ? 0 : paper.returnPct"
          :sub="useLiveAccount ? 'OKX API 账户未实现盈亏' : `浮动盈亏 ${fmtMoney(paper.unrealizedPnl)} USDT`"
        />
        <StatCard
          label="运行中任务"
          :value="stats.runningTasks ?? 0"
          unit="个"
          icon="AlarmClock"
          accent="violet"
          :sub="`共 ${stats.totalTasks ?? 0} 个任务 · 今日成交 ${stats.todayTrades ?? 0} 笔`"
        />
      </div>

      <div class="dash-grid">
        <section class="qts-card dash-curve">
          <div class="card-head">
            <div class="card-title">权益曲线</div>
            <div class="card-extra">
              <div class="seg">
                <button
                  v-for="item in RANGE_TABS"
                  :key="item.value"
                  type="button"
                  class="seg-btn"
                  :class="{ active: range === item.value }"
                  @click="changeRange(item.value)"
                >
                  {{ item.label }}
                </button>
              </div>
            </div>
          </div>
          <div class="card-body card-body--chart">
            <EquityChart v-if="!useLiveAccount && curve.length" :points="curve" :height="288" />
            <EmptyState
              v-else-if="!useLiveAccount"
              compact
              icon="DataLine"
              title="暂无权益数据"
              description="运行策略任务并产生成交后，这里会展示纸面账户的权益变化。"
            >
              <el-button size="small" @click="go('/tasks')">创建策略任务</el-button>
            </EmptyState>
            <EmptyState
              v-else
              compact
              icon="Wallet"
              title="OKX API 账户已接入"
              description="实盘权益曲线采样中。当前账户权益与持仓请查看右侧账户卡与下方持仓。"
            >
              <el-button size="small" @click="go('/trade')">打开交易台</el-button>
            </EmptyState>
          </div>
          <div class="card-foot dash-curve-foot">
            <span>当前权益 <b class="num">{{ fmtMoney(displayEquity) }}</b> USDT</span>
            <span v-if="!useLiveAccount" :class="pnlClass(paper.returnPct)">
              累计收益 <b class="num">{{ fmtPct(paper.returnPct) }}</b>
            </span>
            <span>今日盈亏 <b class="num" :class="pnlClass(stats.todayPnl)">{{ fmtMoney(stats.todayPnl) }}</b> USDT</span>
          </div>
        </section>

        <section v-if="!useLiveAccount" class="qts-card target-card">
          <div class="card-head">
            <div class="card-title">目标进度</div>
            <div class="card-extra">
              <el-tag size="small" effect="plain" type="info">100 → 10000 USDT</el-tag>
            </div>
          </div>
          <div class="card-body target-body">
            <div class="target-headline">
              <span class="target-current num">{{ fmtMoney(target.currentEquity) }}</span>
              <span class="target-unit">USDT</span>
            </div>
            <p class="target-sub">
              目标 <b class="num">{{ fmtMoney(target.target) }}</b> USDT · 初始
              <b class="num">{{ fmtMoney(target.initial) }}</b> USDT
            </p>
            <div class="target-bar" role="progressbar" :aria-valuenow="targetPct" aria-valuemin="0" aria-valuemax="100">
              <span class="target-bar-fill" :style="{ width: `${targetPct}%` }"></span>
            </div>
            <div class="target-meta">
              <span>已完成 <b class="num">{{ targetPct.toFixed(2) }}%</b></span>
              <span>还差 <b class="num">{{ fmtMoney(targetRemaining) }}</b> USDT</span>
            </div>
            <div class="target-metrics">
              <div class="target-metric">
                <span>当前倍数</span>
                <b class="num">{{ targetMultiple }}×</b>
              </div>
              <div class="target-metric">
                <span>浮动盈亏</span>
                <b class="num" :class="pnlClass(paper.unrealizedPnl)">{{ fmtMoney(paper.unrealizedPnl) }}</b>
              </div>
            </div>
            <div class="target-actions">
              <el-button size="small" :icon="DocumentAdd" @click="go('/backtest')">回测验证</el-button>
              <el-button size="small" type="primary" :icon="Plus" @click="go('/tasks')">创建任务</el-button>
            </div>
          </div>
        </section>

        <section v-else class="qts-card target-card">
          <div class="card-head">
            <div class="card-title">OKX API 账户</div>
            <div class="card-extra">
              <el-tag size="small" effect="plain" type="success">已连接</el-tag>
            </div>
          </div>
          <div class="card-body target-body">
            <div class="target-headline">
              <span class="target-current num">{{ fmtMoney(displayEquity) }}</span>
              <span class="target-unit">USDT</span>
            </div>
            <p class="target-sub">账户总权益 · 可用 <b class="num">{{ fmtMoney(displayAvailable) }}</b> USDT</p>
            <div class="target-metrics">
              <div class="target-metric">
                <span>浮动盈亏</span>
                <b class="num" :class="pnlClass(displayUnrealized)">{{ fmtMoney(displayUnrealized) }}</b>
              </div>
              <div class="target-metric">
                <span>运行中任务</span>
                <b class="num">{{ stats.runningTasks ?? 0 }}</b>
              </div>
            </div>
            <div class="target-actions">
              <el-button size="small" :icon="DocumentAdd" @click="go('/backtest')">回测验证</el-button>
              <el-button size="small" type="primary" :icon="Plus" @click="go('/tasks')">创建任务</el-button>
            </div>
          </div>
        </section>
      </div>

      <div class="dash-grid dash-grid--3">
        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">实盘账户</div>
            <div class="card-extra">
              <el-tag v-if="live.connected" size="small" type="success" effect="plain">已连接</el-tag>
              <el-tag v-else-if="live.configured" size="small" type="warning" effect="plain">连接失败</el-tag>
              <el-tag v-else size="small" type="info" effect="plain">未配置</el-tag>
            </div>
          </div>
          <div class="card-body">
            <template v-if="live.configured">
              <div class="kv-list">
                <div class="kv-row">
                  <span>账户总资产</span>
                  <b class="num">{{ fmtMoney(live.totalEquity) }} USDT</b>
                </div>
                <div class="kv-row">
                  <span>可用 USDT</span>
                  <b class="num">{{ fmtMoney(live.availableUsdt) }}</b>
                </div>
                <div class="kv-row">
                  <span>未实现盈亏</span>
                  <b class="num" :class="pnlClass(live.unrealizedPnl)">{{ fmtMoney(live.unrealizedPnl) }}</b>
                </div>
              </div>
              <el-alert
                v-if="live.error"
                class="mini-alert"
                type="error"
                :title="live.error"
                :closable="false"
                show-icon
              />
              <div class="card-link-row">
                <el-button link type="primary" @click="go('/settings')">账户设置</el-button>
              </div>
            </template>
            <EmptyState
              v-else
              compact
              icon="Connection"
              title="尚未配置欧易 API"
              description="配置 API Key 后可同步实盘资产，并在开启实盘交易后运行策略。"
            >
              <el-button size="small" @click="go('/settings')">前往设置</el-button>
            </EmptyState>
          </div>
        </section>

        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">当前持仓</div>
            <div class="card-extra">
              <el-button link type="primary" @click="go('/orders?tab=positions')">查看全部</el-button>
            </div>
          </div>
          <div class="card-body">
            <template v-if="topPositions.length">
              <div v-for="pos in topPositions" :key="pos.id" class="mini-row">
                <div class="mini-row-main">
                  <span class="mini-symbol">{{ pos.instId }}</span>
                  <span class="pill" :class="pos.mode === 'Live' ? 'pill--danger' : 'pill--accent'">{{ modeLabel(pos.mode) }}</span>
                </div>
                <div class="mini-row-side">
                  <b class="num" :class="pnlClass(pos.unrealizedPnl)">{{ fmtMoney(pos.unrealizedPnl) }}</b>
                  <span class="mini-sub num" :class="pnlClass(pos.unrealizedPnlPct)">{{ fmtPct(pos.unrealizedPnlPct) }}</span>
                </div>
              </div>
            </template>
            <EmptyState
              v-else
              compact
              icon="Wallet"
              title="暂无持仓"
              description="策略产生开仓信号后，持仓会显示在这里。"
            />
            <div v-if="topPositions.length" class="card-foot">
              <span>未实现盈亏合计</span>
              <b class="num" :class="pnlClass(paper.unrealizedPnl)">{{ fmtMoney(paper.unrealizedPnl) }} USDT</b>
            </div>
          </div>
        </section>

        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">运行中任务</div>
            <div class="card-extra">
              <el-button link type="primary" @click="go('/tasks')">任务中心</el-button>
            </div>
          </div>
          <div class="card-body">
            <template v-if="runningTasks.length">
              <div v-for="task in runningTasks" :key="task.id" class="mini-row">
                <div class="mini-row-main mini-row-main--stack">
                  <span class="mini-symbol">{{ task.name }}</span>
                  <span class="mini-sub">{{ task.strategyName || task.strategyCode }} · {{ task.instId }}</span>
                </div>
                <div class="mini-row-side">
                  <span class="status-dot status-dot--running"></span>
                  <b class="num" :class="pnlClass(task.totalPnl)">{{ fmtMoney(task.totalPnl) }}</b>
                </div>
              </div>
            </template>
            <EmptyState
              v-else
              compact
              icon="AlarmClock"
              title="没有运行中的任务"
              description="创建策略任务并启动后，会在此展示实时盈亏。"
            >
              <el-button size="small" type="primary" @click="go('/tasks')">创建任务</el-button>
            </EmptyState>
          </div>
        </section>
      </div>

      <section class="qts-card">
        <div class="card-head">
          <div class="card-title">最近订单</div>
          <div class="card-extra">
            <el-button link type="primary" @click="go('/orders')">查看全部</el-button>
          </div>
        </div>
        <div class="card-body card-body--flush">
          <el-table :data="recentOrders" size="small" class="orders-mini-table" style="width: 100%">
            <el-table-column label="时间" width="158">
              <template #default="{ row }">
                <span class="num text-muted">{{ fmtDateTime(row.createdAt) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="任务" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">
                <span>{{ row.taskName || `任务 #${row.taskId}` }}</span>
              </template>
            </el-table-column>
            <el-table-column label="交易对" width="112">
              <template #default="{ row }">
                <span class="mono">{{ row.instId }}</span>
              </template>
            </el-table-column>
            <el-table-column label="方向" width="80">
              <template #default="{ row }">
                <span class="side-tag" :class="row.side === 'Buy' ? 'is-buy' : 'is-sell'">
                  {{ row.side === 'Buy' ? '买入' : '卖出' }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="模式" width="86">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" :type="row.mode === 'Live' ? 'danger' : 'info'">
                  {{ modeLabel(row.mode) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="成交价" width="112" align="right">
              <template #default="{ row }">
                <span class="num">{{ fmtPrice(row.avgFillPrice ?? row.price) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="数量" width="110" align="right">
              <template #default="{ row }">
                <span class="num">{{ fmtNumber(row.filledQuantity ?? row.quantity, 4) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="96">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" :type="orderStatusMeta(row.status).type">
                  {{ orderStatusMeta(row.status).label }}
                </el-tag>
              </template>
            </el-table-column>
            <template #empty>
              <EmptyState compact icon="Tickets" title="暂无订单记录" description="策略任务产生成交后，订单会展示在这里。" />
            </template>
          </el-table>
        </div>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { DocumentAdd, Plus, Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatCard from '@/components/StatCard.vue'
import EquityChart from '@/components/EquityChart.vue'
import EmptyState from '@/components/EmptyState.vue'
import { dashboardApi, taskApi } from '@/api'
import { RANGE_TABS, modeLabel, orderStatusMeta } from '@/utils/constants'
import { fmtDateTime, fmtMoney, fmtNumber, fmtPct, fmtPrice, pnlClass } from '@/utils/format'

const router = useRouter()

const loading = ref(false)
const error = ref('')
const data = ref(null)
const curve = ref([])
const range = ref('30d')
const runningTasks = ref([])
const lastUpdated = ref('')

let refreshTimer = null

const paper = computed(() => data.value?.paper || {})
const stats = computed(() => data.value?.stats || {})
const target = computed(() => data.value?.target || {})
const live = computed(() => data.value?.live || {})
const useLiveAccount = computed(() => Boolean(live.value.configured && live.value.connected))
const displayEquity = computed(() => (useLiveAccount.value ? Number(live.value.totalEquity ?? 0) : Number(paper.value.equity ?? 0)))
const displayAvailable = computed(() => (useLiveAccount.value ? Number(live.value.availableUsdt ?? 0) : Number(paper.value.available ?? 0)))
const displayUnrealized = computed(() => (useLiveAccount.value ? Number(live.value.unrealizedPnl ?? 0) : Number(paper.value.unrealizedPnl ?? 0)))
const recentOrders = computed(() => data.value?.recentOrders || [])
const topPositions = computed(() => data.value?.topPositions || [])
const curveSpark = computed(() => curve.value.slice(-40).map((point) => Number(point.equity ?? 0)))

const targetPct = computed(() => {
  const pct = Number(target.value.progressPct ?? 0)
  if (!Number.isFinite(pct)) return 0
  return Math.min(100, Math.max(0, pct))
})

const targetRemaining = computed(() => {
  const targetValue = Number(target.value.target ?? 0)
  const current = Number(target.value.currentEquity ?? 0)
  return Math.max(0, targetValue - current)
})

const targetMultiple = computed(() => {
  const initial = Number(target.value.initial ?? 0)
  const current = Number(target.value.currentEquity ?? 0)
  if (!initial) return '—'
  return (current / initial).toFixed(2)
})

function pnlAccent(value) {
  const num = Number(value ?? 0)
  if (num > 0) return 'green'
  if (num < 0) return 'red'
  return 'cyan'
}

function nowText() {
  return new Date().toLocaleTimeString('zh-CN', { hour12: false })
}

function go(path) {
  router.push(path)
}

function normalizeCurve(payload) {
  if (Array.isArray(payload)) return payload
  if (Array.isArray(payload?.items)) return payload.items
  if (Array.isArray(payload?.equityCurve)) return payload.equityCurve
  return []
}

async function loadOverview(silent = false) {
  if (!silent) loading.value = true
  try {
    const res = await dashboardApi.overview()
    data.value = res || {}
    error.value = ''
    lastUpdated.value = nowText()
  } catch (err) {
    error.value = err?.message || '仪表盘数据加载失败，请稍后重试'
  } finally {
    loading.value = false
  }
}

async function loadCurve(silent = false) {
  try {
    const res = await dashboardApi.equityCurve(range.value)
    curve.value = normalizeCurve(res)
  } catch (err) {
    if (!silent) ElMessage.error(err?.message || '权益曲线加载失败')
  }
}

async function loadRunningTasks() {
  try {
    const res = await taskApi.list({ page: 1, pageSize: 5, status: 'Running' })
    runningTasks.value = res?.items || []
  } catch {
    runningTasks.value = []
  }
}

function changeRange(value) {
  if (range.value === value) return
  range.value = value
  loadCurve()
}

async function refreshAll() {
  await Promise.all([loadOverview(), loadRunningTasks(), loadCurve()])
}

function startAutoRefresh() {
  refreshTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    loadOverview(true)
    loadRunningTasks()
    loadCurve(true)
  }, 30000)
}

onMounted(() => {
  refreshAll()
  startAutoRefresh()
})

onBeforeUnmount(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
})
</script>

<style scoped>
.dash-skeleton {
  padding: 24px;
}

.dash-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.9fr) minmax(0, 1fr);
  gap: 16px;
}

.dash-grid > .qts-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.dash-grid > .qts-card > .card-body {
  flex: 1;
}

.dash-grid--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.dash-curve-foot {
  flex-wrap: wrap;
  gap: 9px 20px;
}

.dash-curve-foot b {
  color: var(--qts-text);
  font-weight: 600;
}

/* 目标进度卡 */
.target-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.target-headline {
  display: flex;
  align-items: baseline;
  gap: 7px;
}

.target-current {
  font-size: 30px;
  font-weight: 700;
  color: #f8fafc;
  letter-spacing: 0.5px;
}

.target-unit {
  font-size: 12px;
  color: var(--qts-text-dim);
  letter-spacing: 1px;
}

.target-sub {
  font-size: 12px;
  color: var(--qts-text-muted);
}

.target-sub b {
  color: var(--qts-text);
  font-weight: 600;
}

.target-bar {
  position: relative;
  height: 9px;
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.12);
  overflow: hidden;
}

.target-bar-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, #0891b2 0%, #22d3ee 55%, #67e8f9 100%);
  box-shadow: 0 0 14px rgba(34, 211, 238, 0.55);
  transition: width 0.6s ease;
}

.target-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-size: 11.5px;
  color: var(--qts-text-dim);
}

.target-meta b {
  color: #67e8f9;
  margin-left: 3px;
}

.target-metrics {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  padding-top: 4px;
}

.target-metric {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 10px 12px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.1);
  background: rgba(148, 163, 184, 0.035);
}

.target-metric span {
  font-size: 11px;
  color: var(--qts-text-dim);
}

.target-metric b {
  font-size: 15px;
  color: var(--qts-text);
  font-weight: 600;
}

.target-actions {
  display: flex;
  gap: 8px;
  margin-top: auto;
  padding-top: 4px;
}

.target-actions .el-button {
  flex: 1;
}

/* 迷你行 */
.mini-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 0;
  border-bottom: 1px solid rgba(148, 163, 184, 0.07);
}

.mini-row:last-of-type {
  border-bottom: none;
}

.mini-row-main {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.mini-row-main--stack {
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
}

.mini-symbol {
  font-size: 13px;
  font-weight: 600;
  color: var(--qts-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.mini-sub {
  font-size: 11px;
  color: var(--qts-text-dim);
}

.mini-row-side {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.mini-row-side b {
  font-size: 13px;
  font-weight: 600;
}

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
}

.status-dot--running {
  background: var(--qts-success);
  box-shadow: 0 0 0 3px rgba(52, 211, 153, 0.14);
  animation: dot-pulse 1.8s ease-in-out infinite;
}

.kv-list {
  display: flex;
  flex-direction: column;
  gap: 11px;
}

.kv-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 12.5px;
  color: var(--qts-text-muted);
}

.kv-row b {
  color: var(--qts-text);
  font-weight: 600;
}

.mini-alert {
  margin-top: 12px;
}

.card-link-row {
  display: flex;
  justify-content: flex-end;
  margin-top: 10px;
}

/* 订单迷你表 */
.orders-mini-table :deep(.el-table__empty-block) {
  min-height: 180px;
}

.mono {
  font-family: var(--qts-mono);
  font-size: 12px;
  color: #cbd5e1;
}

.side-tag {
  display: inline-flex;
  align-items: center;
  height: 20px;
  padding: 0 7px;
  border-radius: 5px;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.4px;
}

.side-tag.is-buy {
  color: #6ee7b7;
  background: rgba(52, 211, 153, 0.1);
  border: 1px solid rgba(52, 211, 153, 0.22);
}

.side-tag.is-sell {
  color: #fda4af;
  background: rgba(251, 113, 133, 0.1);
  border: 1px solid rgba(251, 113, 133, 0.22);
}

@media (max-width: 1360px) {
  .dash-grid--3 {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .dash-grid--3 > .qts-card:last-child {
    grid-column: 1 / -1;
  }
}

@media (max-width: 1100px) {
  .dash-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 720px) {
  .dash-grid--3 {
    grid-template-columns: minmax(0, 1fr);
  }

  .dash-grid--3 > .qts-card:last-child {
    grid-column: auto;
  }

  .target-current {
    font-size: 26px;
  }
}
</style>
