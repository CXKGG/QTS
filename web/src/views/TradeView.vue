<template>
  <div class="page-stack">
    <PageHeader title="交易台" subtitle="OKX API 账户 · USDT 永续合约 · 多空 / 杠杆交易">
      <template #tag>
        <el-tag v-if="!account.configured" type="info" effect="plain" size="small">未配置 API</el-tag>
        <el-tag v-else-if="!account.connected" type="danger" effect="plain" size="small">API 连接失败</el-tag>
        <el-tag v-else :type="account.simulated ? 'warning' : 'danger'" effect="plain" size="small">
          {{ account.simulated ? 'OKX API · 模拟环境' : 'OKX API · 实盘账户' }}
        </el-tag>
      </template>
      <el-button :icon="Refresh" :loading="loading" @click="load(true)">刷新</el-button>
      <el-button type="primary" :icon="Plus" @click="router.push('/tasks?create=1')">新建策略任务</el-button>
    </PageHeader>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="page-alert" />
    <el-alert
      v-if="account.error"
      :title="account.error"
      type="error"
      show-icon
      :closable="false"
      class="page-alert"
    />

    <div v-if="initialLoading" class="qts-card skeleton-card">
      <el-skeleton :rows="10" animated />
    </div>

    <template v-else-if="overview">
      <div class="stat-grid trade-stats">
        <StatCard
          label="账户总权益"
          :value="fmtMoney(account.totalEquity, accountDigits)"
          unit="USDT"
          icon="Wallet"
          accent="cyan"
          :sub="`${account.accountType || 'OKX API 账户'} · ${account.environment || '模拟环境'}`"
        />
        <StatCard
          label="可用保证金"
          :value="fmtMoney(account.available, accountDigits)"
          unit="USDT"
          icon="Coin"
          accent="blue"
          :sub="`占用 ${fmtMoney(account.usedMargin)} USDT`"
        />
        <StatCard
          label="占用保证金"
          :value="fmtMoney(account.usedMargin, accountDigits)"
          unit="USDT"
          icon="Money"
          accent="violet"
          :sub="`持仓市值 ${fmtMoney(account.positionValue)} USDT`"
        />
        <StatCard
          label="浮动盈亏"
          :value="fmtMoney(account.unrealizedPnl)"
          unit="USDT"
          icon="TrendCharts"
          :accent="pnlAccent(account.unrealizedPnl)"
          :value-color="pnlColor(account.unrealizedPnl)"
          :sub="`未平仓 ${positions.length} 个`"
        />
        <StatCard
          label="已实现盈亏"
          :value="fmtMoney(account.realizedPnl)"
          unit="USDT"
          icon="DataLine"
          :accent="pnlAccent(account.realizedPnl)"
          :value-color="pnlColor(account.realizedPnl)"
          :sub="'手动平仓累计'"
        />
      </div>

      <div class="trade-grid">
        <div class="trade-main">
          <section class="qts-card trade-chart-card">
            <div class="card-head trade-chart-head">
              <div class="trade-chart-title">
                <span class="card-title">{{ currentInstName }} · K 线</span>
                <span class="text-dim">{{ orderForm.instId }} · {{ chartBarLabel }}</span>
              </div>
              <div class="trade-chart-actions">
                <div class="chart-bar-tabs">
                  <button
                    v-for="item in CHART_BARS"
                    :key="item.value"
                    type="button"
                    class="chart-bar-btn"
                    :class="{ active: chartBar === item.value }"
                    @click="changeChartBar(item.value)"
                  >
                    {{ item.label }}
                  </button>
                </div>
                <el-button link :icon="Refresh" :loading="candlesLoading" @click="loadCandles(false)">
                  刷新
                </el-button>
              </div>
            </div>
            <div class="card-body card-body--flush">
              <div v-if="candlesLoading && !candles.length" class="trade-chart-loading">
                <el-skeleton :rows="6" animated />
              </div>
              <KlineChart v-else-if="candles.length" :candles="candles" :height="308" :visible-bars="60" />
              <EmptyState
                v-else
                icon="TrendCharts"
                :title="chartError || '暂无 K 线数据'"
                description="切换周期或刷新后重试；离线时后端会返回示例行情"
              />
            </div>
          </section>

          <section class="qts-card">
            <div class="card-head">
              <span class="card-title">当前持仓</span>
              <span class="card-extra">手动交易仓位可随时平仓 · 策略任务仓位只读</span>
            </div>
            <div class="card-body card-body--flush">
              <el-table v-if="positions.length" :data="positions" row-key="id" size="small">
                <el-table-column label="交易对" min-width="130">
                  <template #default="{ row }">
                    <div class="sym-cell">
                      <b>{{ row.instId }}</b>
                      <span class="text-dim">{{ modeLabel(row.mode) }}</span>
                    </div>
                  </template>
                </el-table-column>
                <el-table-column label="方向" width="76" align="center">
                  <template #default="{ row }">
                    <el-tag size="small" effect="plain" :type="sideMeta(row.side).type">
                      {{ sideMeta(row.side).label }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="数量" width="108" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ fmtNumber(row.quantity, 6) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="开仓均价" width="106" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ fmtPrice(row.avgEntryPrice) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="标记价格" width="106" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ fmtPrice(row.lastPrice) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="杠杆" width="70" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ Number(row.leverage ?? 1) }}×</span>
                  </template>
                </el-table-column>
                <el-table-column label="保证金" width="104" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ fmtMoney(row.margin) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="未实现盈亏" width="136" align="right">
                  <template #default="{ row }">
                    <div class="num pnl-cell" :style="{ color: pnlColor(row.unrealizedPnl) }">
                      <b>{{ fmtMoney(row.unrealizedPnl) }}</b>
                      <span>{{ fmtPct(row.unrealizedPnlPct, 2) }}</span>
                    </div>
                  </template>
                </el-table-column>
                <el-table-column label="止损 / 止盈" width="156" align="center">
                  <template #default="{ row }">
                    <span class="num text-dim">{{ riskText(row) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="开仓时间" width="150">
                  <template #default="{ row }">
                    <span class="text-dim">{{ fmtDateTime(row.openedAt) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="操作" width="132" fixed="right" align="right">
                  <template #default="{ row }">
                    <el-button
                      v-if="isManualPosition(row)"
                      link
                      type="danger"
                      :loading="actionLoading === row.id"
                      @click="closePosition(row)"
                    >
                      平仓
                    </el-button>
                    <span v-else class="task-badge text-dim">
                      {{ row.taskName || `任务 #${row.taskId}` }}
                    </span>
                  </template>
                </el-table-column>
              </el-table>
              <EmptyState
                v-else
                icon="Wallet"
                title="暂无持仓"
                description="使用右侧下单面板开仓后，持仓会显示在这里"
              />
            </div>
          </section>

          <section class="qts-card">
            <div class="card-head">
              <span class="card-title">策略任务</span>
              <el-button link type="primary" @click="router.push('/tasks')">全部任务</el-button>
            </div>
            <div class="card-body card-body--flush">
              <el-table v-if="tasks.length" :data="tasks" row-key="id" size="small">
                <el-table-column label="任务" min-width="156">
                  <template #default="{ row }">
                    <div class="sym-cell">
                      <b>{{ row.name }}</b>
                      <span class="text-dim">#{{ row.id }}</span>
                    </div>
                  </template>
                </el-table-column>
                <el-table-column label="状态" width="102">
                  <template #default="{ row }">
                    <el-tag size="small" effect="plain" :type="taskStatusMeta(row.status).type">
                      <span class="status-dot" :class="{ 'is-running': row.status === 'Running' }" />
                      {{ taskStatusMeta(row.status).label }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="策略" min-width="112">
                  <template #default="{ row }">
                    <span class="text-dim">{{ row.strategyName || row.strategyCode || '—' }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="分配资金" width="104" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ fmtMoney(row.allocatedCapital) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="杠杆" width="66" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ Math.max(1, Number(row.leverage || 1)) }}×</span>
                  </template>
                </el-table-column>
                <el-table-column label="总盈亏" width="112" align="right">
                  <template #default="{ row }">
                    <span class="num" :style="{ color: pnlColor(row.totalPnl) }">{{ fmtMoney(row.totalPnl) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="当前持仓" width="128" align="right">
                  <template #default="{ row }">
                    <span v-if="row.positionQty" class="num">
                      {{ fmtNumber(row.positionQty, 4) }}
                      <span class="text-dim">@ {{ fmtPrice(row.positionAvgPrice) }}</span>
                    </span>
                    <span v-else class="text-dim">—</span>
                  </template>
                </el-table-column>
                <el-table-column label="最近信号" width="96" align="center">
                  <template #default="{ row }">
                    <span class="text-dim">{{ row.lastSignal ? signalMeta(row.lastSignal).label : '—' }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="操作" width="96" fixed="right" align="right">
                  <template #default="{ row }">
                    <el-button
                      link
                      :type="row.status === 'Running' ? 'danger' : 'primary'"
                      :loading="actionLoading === row.id"
                      @click="toggleTask(row)"
                    >
                      {{ row.status === 'Running' ? '停止' : '启动' }}
                    </el-button>
                  </template>
                </el-table-column>
              </el-table>
              <EmptyState
                v-else
                icon="AlarmClock"
                title="暂无策略任务"
                description="创建一个自动交易任务，与手动仓位在同一 OKX API 账户中运行"
              >
                <el-button type="primary" :icon="Plus" @click="router.push('/tasks?create=1')">
                  创建策略任务
                </el-button>
              </EmptyState>
            </div>
          </section>
        </div>

        <aside class="order-panel qts-card">
          <div class="card-head">
            <span class="card-title">手动下单</span>
            <el-tag size="small" :type="account.simulated ? 'warning' : 'danger'" effect="plain">
              {{ account.simulated ? 'API 模拟环境' : '实盘账户' }}
            </el-tag>
          </div>
          <div class="card-body">
            <div v-if="instruments.length" class="ticker-strip">
              <div>
                <div class="ticker-price num">{{ fmtPrice(ticker.last) }}</div>
                <div class="ticker-change num" :class="pnlClass(ticker.change24hPct)">
                  {{ fmtPct(ticker.change24hPct, 2) }} · {{ orderForm.instId }}
                </div>
              </div>
              <div class="ticker-meta">
                <span>24h 高 {{ fmtPrice(ticker.high24h) }}</span>
                <span>24h 低 {{ fmtPrice(ticker.low24h) }}</span>
                <span>成交额 {{ fmtCompact(ticker.volume24h) }}</span>
                <span>更新 {{ fmtClock(ticker.time) }}</span>
              </div>
            </div>

            <el-form label-position="top" class="order-form" @submit.prevent>
              <el-form-item label="交易对">
                <el-select v-model="orderForm.instId" style="width: 100%" @change="onInstrumentChange">
                  <el-option
                    v-for="item in instruments"
                    :key="item.instId"
                    :label="item.name || item.instId"
                    :value="item.instId"
                  />
                </el-select>
              </el-form-item>

              <el-form-item label="方向">
                <div class="direction-toggle">
                  <button
                    type="button"
                    class="direction-btn is-long"
                    :class="{ active: orderForm.direction === 'long' }"
                    @click="orderForm.direction = 'long'"
                  >
                    做多 · Long
                  </button>
                  <button
                    type="button"
                    class="direction-btn is-short"
                    :class="{ active: orderForm.direction === 'short' }"
                    @click="orderForm.direction = 'short'"
                  >
                    做空 · Short
                  </button>
                </div>
              </el-form-item>

              <el-form-item label="保证金 (USDT)">
                <el-input-number
                  v-model="orderForm.margin"
                  :min="1"
                  :max="Math.max(1, availableMargin)"
                  :step="10"
                  :precision="2"
                  controls-position="right"
                  style="width: 100%"
                />
                <div class="quick-row">
                  <button
                    v-for="pct in [0.25, 0.5, 1]"
                    :key="pct"
                    type="button"
                    class="quick-btn"
                    @click="setMarginPct(pct)"
                  >
                    {{ pct === 1 ? '全部' : `${pct * 100}%` }}
                  </button>
                  <span class="quick-avail">可用 {{ fmtMoney(account.available) }} USDT</span>
                </div>
              </el-form-item>

              <el-form-item :label="`杠杆 (${orderForm.leverage}×)`">
                <div class="leverage-row">
                  <el-slider
                    v-model="orderForm.leverage"
                    :min="1"
                    :max="20"
                    :step="1"
                    :show-tooltip="false"
                    class="lev-slider"
                  />
                  <el-input-number
                    v-model="orderForm.leverage"
                    :min="1"
                    :max="20"
                    :step="1"
                    size="small"
                    controls-position="right"
                    class="lev-input"
                  />
                </div>
              </el-form-item>

              <div class="form-grid-2 compact-grid">
                <el-form-item label="滑点 (%)">
                  <el-input-number
                    v-model="orderForm.slippagePct"
                    :min="0"
                    :max="5"
                    :step="0.01"
                    :precision="2"
                    controls-position="right"
                    style="width: 100%"
                  />
                </el-form-item>
                <el-form-item label="止损 (%)">
                  <el-input-number
                    v-model="orderForm.stopLossPct"
                    :min="0"
                    :max="50"
                    :step="0.5"
                    :precision="2"
                    controls-position="right"
                    style="width: 100%"
                  />
                </el-form-item>
                <el-form-item label="止盈 (%)">
                  <el-input-number
                    v-model="orderForm.takeProfitPct"
                    :min="0"
                    :max="200"
                    :step="0.5"
                    :precision="2"
                    controls-position="right"
                    style="width: 100%"
                  />
                </el-form-item>
              </div>

              <div class="estimate-box">
                <div class="estimate-row">
                  <span>名义价值</span>
                  <b class="num">{{ fmtMoney(estNotional) }} USDT</b>
                </div>
                <div class="estimate-row">
                  <span>预计数量</span>
                  <b class="num">{{ fmtNumber(estQty, 6) }} {{ baseCcy }}</b>
                </div>
                <div class="estimate-row">
                  <span>强平参考价</span>
                  <b v-if="orderForm.leverage > 1" class="num">≈ {{ fmtPrice(liquidationPrice) }}</b>
                  <b v-else class="num text-muted">1× 无强平</b>
                </div>
              </div>

              <p v-if="formError" class="inline-error">{{ formError }}</p>

              <el-button
                :type="orderForm.direction === 'long' ? 'success' : 'danger'"
                size="large"
                class="submit-btn"
                :loading="submitting"
                :disabled="!account.connected"
                @click="submitOrder"
              >
                {{ orderForm.direction === 'long' ? '买入开多' : '卖出开空' }} · {{ orderForm.leverage }}×
              </el-button>
              <p class="panel-tip">
                {{ account.simulated
                  ? '当前为 OKX API 模拟环境，订单会发送到你的模拟账户，不使用真实资金。'
                  : '当前为 OKX API 实盘账户，订单会使用真实资金下单，请确认保证金与杠杆。' }}
                手动仓位通过 USDT 永续逐仓合约开仓，平仓使用 reduceOnly。
              </p>
            </el-form>
          </div>
        </aside>
      </div>
    </template>

    <EmptyState v-else icon="Money" title="交易台数据不可用" description="请确认后端服务已启动后重新加载">
      <el-button type="primary" @click="load()">重新加载</el-button>
    </EmptyState>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatCard from '@/components/StatCard.vue'
import EmptyState from '@/components/EmptyState.vue'
import KlineChart from '@/components/KlineChart.vue'
import { marketApi, taskApi, tradeApi } from '@/api'
import {
  fmtClock,
  fmtCompact,
  fmtDateTime,
  fmtMoney,
  fmtNumber,
  fmtPct,
  fmtPrice,
  isNil,
  pnlClass
} from '@/utils/format'
import { modeLabel, sideMeta, signalMeta, taskStatusMeta } from '@/utils/constants'
import { getKlineColors } from '@/utils/chartColors'

const router = useRouter()
const POLL_MS = 8000
const CHART_POLL_MS = 30000
const CHART_BARS = [
  { value: '15m', label: '15分' },
  { value: '1H', label: '1时' },
  { value: '4H', label: '4时' },
  { value: '1D', label: '1日' }
]

const loading = ref(false)
const initialLoading = ref(true)
const submitting = ref(false)
const actionLoading = ref(0)
const error = ref('')
const formError = ref('')
const overview = ref(null)
const chartBar = ref('1H')
const candles = ref([])
const candlesLoading = ref(false)
const chartError = ref('')
const klineColors = ref(getKlineColors())

const orderForm = reactive({
  instId: 'BTC-USDT',
  direction: 'long',
  margin: 10,
  leverage: 1,
  slippagePct: 0.05,
  stopLossPct: 0,
  takeProfitPct: 0
})

const account = computed(() => overview.value?.account || {})
const instruments = computed(() => overview.value?.instruments || [])
const ticker = computed(() => overview.value?.ticker || {})
const positions = computed(() => overview.value?.positions || [])
const tasks = computed(() => overview.value?.tasks || [])
const currentInstName = computed(
  () => instruments.value.find((item) => item.instId === orderForm.instId)?.name || orderForm.instId
)
const chartBarLabel = computed(
  () => CHART_BARS.find((item) => item.value === chartBar.value)?.label || chartBar.value
)
const availableMargin = computed(() => Number(account.value.available || 0))
const accountDigits = computed(() => (Number(account.value.totalEquity || 0) < 1 ? 4 : 2))
const baseCcy = computed(() => String(orderForm.instId || '').split('-')[0] || 'USDT')
const estNotional = computed(() => Number(orderForm.margin || 0) * Number(orderForm.leverage || 1))
const estQty = computed(() => {
  const last = Number(ticker.value.last || 0)
  return last > 0 ? estNotional.value / last : 0
})
const liquidationPrice = computed(() => {
  const last = Number(ticker.value.last || 0)
  if (!last) return null
  const leverage = Number(orderForm.leverage || 1)
  return orderForm.direction === 'long'
    ? last * (1 - 1 / leverage)
    : last * (1 + 1 / leverage)
})

let pollTimer = null
let candleTimer = null

function pnlAccent(value) {
  const num = Number(value)
  if (!Number.isFinite(num) || num === 0) return 'blue'
  return num > 0 ? 'green' : 'red'
}

function pnlColor(value) {
  const num = Number(value)
  if (!Number.isFinite(num) || num === 0) return ''
  return num > 0 ? klineColors.value.up : klineColors.value.down
}

function syncKlineColors(event) {
  klineColors.value = event?.detail || getKlineColors()
}

function isManualPosition(row) {
  return Number(row?.taskId) === 0
}

function riskText(row) {
  const stop = row?.stopLoss
  const take = row?.takeProfit
  const parts = []
  if (!isNil(stop) && Number(stop) > 0) parts.push(`止 ${fmtPrice(stop)}`)
  if (!isNil(take) && Number(take) > 0) parts.push(`盈 ${fmtPrice(take)}`)
  return parts.length ? parts.join(' / ') : '—'
}

function setMarginPct(pct) {
  const available = availableMargin.value
  if (available <= 0) return
  const value = Math.floor(available * pct * 100) / 100
  orderForm.margin = Math.max(1, value)
}

function onInstrumentChange() {
  formError.value = ''
  candles.value = []
  chartError.value = ''
  load(true)
  loadCandles(false)
}

function sortCandles(list) {
  return [...list].sort((a, b) => Number(a.time) - Number(b.time))
}

async function loadCandles(silent = false) {
  if (!orderForm.instId) return
  if (!silent) candlesLoading.value = true
  try {
    const res = await marketApi.candles({
      instId: orderForm.instId,
      bar: chartBar.value,
      limit: 300
    })
    candles.value = Array.isArray(res) ? sortCandles(res) : []
    chartError.value = ''
  } catch (err) {
    if (!silent) {
      candles.value = []
      chartError.value = err?.message || 'K 线数据加载失败'
    }
  } finally {
    candlesLoading.value = false
  }
}

function changeChartBar(value) {
  if (chartBar.value === value) return
  chartBar.value = value
  loadCandles(false)
}

async function load(silent = false) {
  if (!silent) loading.value = true
  try {
    const res = await tradeApi.overview({ instId: orderForm.instId })
    overview.value = res || null
    error.value = ''
  } catch (err) {
    error.value = err?.message || '交易台数据加载失败'
  } finally {
    loading.value = false
    initialLoading.value = false
  }
}

function validateOrder() {
  const margin = Number(orderForm.margin)
  if (!Number.isFinite(margin) || margin < 1) return '保证金至少 1 USDT'
  if (margin > availableMargin.value) {
    return `保证金超过可用余额（可用 ${fmtMoney(availableMargin.value)} USDT）`
  }
  const leverage = Number(orderForm.leverage)
  if (!Number.isFinite(leverage) || leverage < 1 || leverage > 20) return '杠杆需在 1–20 倍之间'
  const slippage = Number(orderForm.slippagePct)
  if (!Number.isFinite(slippage) || slippage < 0 || slippage > 5) return '滑点需在 0–5% 之间'
  const stop = Number(orderForm.stopLossPct)
  if (!Number.isFinite(stop) || stop < 0 || stop > 50) return '止损需在 0–50% 之间'
  const take = Number(orderForm.takeProfitPct)
  if (!Number.isFinite(take) || take < 0 || take > 200) return '止盈需在 0–200% 之间'
  return ''
}

async function submitOrder() {
  if (submitting.value) return
  formError.value = validateOrder()
  if (formError.value) return
  const directionLabel = orderForm.direction === 'long' ? '做多' : '做空'
  const envLabel = account.value.simulated ? 'OKX 模拟环境' : 'OKX 实盘账户（真实资金）'
  try {
    await ElMessageBox.confirm(
      `即将在 ${envLabel} 以 ${orderForm.leverage}× 杠杆${directionLabel} ${orderForm.instId}，占用保证金 ${fmtMoney(orderForm.margin)} USDT，名义价值约 ${fmtMoney(estNotional.value)} USDT。`,
      account.value.simulated ? '确认模拟下单' : '确认实盘下单',
      { type: 'warning', confirmButtonText: '确认下单', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  submitting.value = true
  try {
    const res = await tradeApi.open({
      instId: orderForm.instId,
      direction: orderForm.direction,
      margin: Number(orderForm.margin),
      leverage: Number(orderForm.leverage),
      slippagePct: Number(orderForm.slippagePct),
      stopLossPct: Number(orderForm.stopLossPct || 0),
      takeProfitPct: Number(orderForm.takeProfitPct || 0)
    })
    ElMessage.success(res?.message || '下单成功')
    await load(true)
  } catch (err) {
    ElMessage.error(err?.message || '下单失败')
  } finally {
    submitting.value = false
  }
}

async function closePosition(row) {
  if (!row || actionLoading.value) return
  const directionLabel = String(row.side).toLowerCase() === 'short' ? '空头' : '多头'
  try {
    await ElMessageBox.confirm(
      `将在 ${account.value.simulated ? 'OKX 模拟环境' : 'OKX 实盘账户'} 按市价 reduceOnly 平仓 ${row.instId} ${directionLabel} ${fmtNumber(row.quantity, 6)}。`,
      '确认平仓',
      { type: 'warning', confirmButtonText: '确认平仓', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  actionLoading.value = row.id
  try {
    const res = await tradeApi.close({
      positionId: row.id,
      slippagePct: Number(orderForm.slippagePct)
    })
    ElMessage.success(res?.message || '平仓成功')
    await load(true)
  } catch (err) {
    ElMessage.error(err?.message || '平仓失败')
  } finally {
    actionLoading.value = 0
  }
}

async function toggleTask(row) {
  if (!row || actionLoading.value) return
  const running = row.status === 'Running'
  try {
    await ElMessageBox.confirm(
      running ? `确认停止策略任务「${row.name}」？停止后不再产生新信号。` : `确认启动策略任务「${row.name}」？`,
      running ? '停止任务' : '启动任务',
      { type: 'warning', confirmButtonText: running ? '停止' : '启动', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  actionLoading.value = row.id
  try {
    if (running) await taskApi.stop(row.id)
    else await taskApi.start(row.id)
    ElMessage.success(running ? '任务已停止' : '任务已启动')
    await load(true)
  } catch (err) {
    ElMessage.error(err?.message || '操作失败')
  } finally {
    actionLoading.value = 0
  }
}

function startPolling() {
  stopPolling()
  pollTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    load(true)
  }, POLL_MS)
  candleTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    loadCandles(true)
  }, CHART_POLL_MS)
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
  if (candleTimer) {
    clearInterval(candleTimer)
    candleTimer = null
  }
}

onMounted(async () => {
  window.addEventListener('qts:kline-colors', syncKlineColors)
  await Promise.all([load(), loadCandles(false)])
  startPolling()
})

onBeforeUnmount(() => {
  window.removeEventListener('qts:kline-colors', syncKlineColors)
  stopPolling()
})
</script>

<style scoped>
.page-alert {
  margin-bottom: 2px;
}

.skeleton-card {
  padding: 26px;
  min-height: 300px;
}

.trade-stats {
  grid-template-columns: repeat(5, minmax(0, 1fr));
}

.trade-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 392px;
  gap: 16px;
  align-items: start;
}

.trade-main {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.trade-chart-card {
  overflow: hidden;
}

.trade-chart-head {
  gap: 12px;
  flex-wrap: wrap;
}

.trade-chart-title {
  display: flex;
  align-items: baseline;
  gap: 10px;
  min-width: 0;
}

.trade-chart-title .text-dim {
  font-size: 11px;
}

.trade-chart-actions {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-left: auto;
}

.chart-bar-tabs {
  display: inline-flex;
  padding: 2px;
  gap: 2px;
  border: 1px solid var(--qts-border, rgba(148, 163, 184, 0.16));
  border-radius: 7px;
  background: rgba(15, 23, 42, 0.35);
}

.chart-bar-btn {
  border: 0;
  background: transparent;
  color: var(--qts-text-dim, #8fa3bf);
  font-size: 11.5px;
  line-height: 1;
  padding: 6px 10px;
  border-radius: 5px;
  cursor: pointer;
  transition: all 0.16s ease;
}

.chart-bar-btn:hover {
  color: #e2e8f0;
}

.chart-bar-btn.active {
  color: #06121f;
  background: #38bdf8;
  font-weight: 600;
}

.trade-chart-loading {
  padding: 18px 16px;
  min-height: 308px;
}

.trade-chart-card .card-body--flush {
  padding: 2px 8px 8px;
}

.order-panel {
  position: sticky;
  top: 12px;
}

.sym-cell {
  display: flex;
  flex-direction: column;
  gap: 1px;
  line-height: 1.25;
}

.sym-cell b {
  font-weight: 600;
  font-size: 12.5px;
}

.sym-cell .text-dim {
  font-size: 11px;
}

.pnl-cell {
  display: flex;
  flex-direction: column;
  gap: 1px;
  line-height: 1.25;
}

.pnl-cell b {
  font-weight: 600;
}

.pnl-cell span {
  font-size: 10.5px;
  opacity: 0.85;
}

.status-dot {
  display: inline-block;
  width: 5px;
  height: 5px;
  margin-right: 5px;
  border-radius: 50%;
  background: currentColor;
  vertical-align: middle;
}

.status-dot.is-running {
  animation: pulse 1.8s ease-in-out infinite;
}

@keyframes pulse {
  0%,
  100% {
    box-shadow: 0 0 0 0 rgba(52, 211, 153, 0.5);
  }
  50% {
    box-shadow: 0 0 0 3px rgba(52, 211, 153, 0);
  }
}

.task-badge {
  font-size: 11px;
  white-space: nowrap;
}

.ticker-strip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  margin-bottom: 14px;
  border-radius: 12px;
  border: 1px solid rgba(34, 211, 238, 0.16);
  background: linear-gradient(135deg, rgba(34, 211, 238, 0.08), rgba(34, 211, 238, 0.02));
}

.ticker-price {
  font-size: 23px;
  font-weight: 650;
  letter-spacing: -0.01em;
  line-height: 1.15;
}

.ticker-change {
  font-size: 11.5px;
  margin-top: 2px;
}

.ticker-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 10.5px;
  color: var(--qts-text-dim);
  text-align: right;
  line-height: 1.4;
}

.order-form :deep(.el-form-item) {
  margin-bottom: 13px;
}

.direction-toggle {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  width: 100%;
}

.direction-btn {
  height: 38px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background: rgba(148, 163, 184, 0.05);
  color: var(--qts-text-dim);
  font-family: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition:
    color 0.16s ease,
    border-color 0.16s ease,
    background 0.16s ease,
    box-shadow 0.16s ease;
}

.direction-btn:hover {
  color: var(--qts-text);
  border-color: rgba(148, 163, 184, 0.3);
}

.direction-btn.is-long.active {
  color: #34d399;
  border-color: rgba(52, 211, 153, 0.55);
  background: rgba(52, 211, 153, 0.13);
  box-shadow: 0 8px 22px rgba(52, 211, 153, 0.12);
}

.direction-btn.is-short.active {
  color: #fb7185;
  border-color: rgba(251, 113, 133, 0.55);
  background: rgba(251, 113, 133, 0.13);
  box-shadow: 0 8px 22px rgba(251, 113, 133, 0.12);
}

.quick-row {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  margin-top: 6px;
}

.quick-btn {
  height: 24px;
  padding: 0 9px;
  border-radius: 6px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background: transparent;
  color: var(--qts-text-muted);
  font-family: inherit;
  font-size: 11px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.quick-btn:hover {
  color: #67e8f9;
  border-color: rgba(34, 211, 238, 0.45);
}

.quick-avail {
  margin-left: auto;
  font-size: 10.5px;
  color: var(--qts-text-dim);
}

.leverage-row {
  display: flex;
  align-items: center;
  gap: 14px;
  width: 100%;
}

.lev-slider {
  flex: 1;
}

.lev-input {
  width: 92px;
  flex: none;
}

.compact-grid {
  gap: 0 12px;
}

.estimate-box {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 11px 13px;
  margin: 4px 0 12px;
  border-radius: 12px;
  border: 1px dashed rgba(148, 163, 184, 0.18);
  background: rgba(148, 163, 184, 0.04);
}

.estimate-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
  color: var(--qts-text-muted);
}

.estimate-row b {
  color: var(--qts-text);
  font-weight: 600;
}

.submit-btn {
  width: 100%;
  height: 42px;
  font-size: 14px;
}

.panel-tip {
  margin: 10px 0 0;
  font-size: 10.5px;
  line-height: 1.6;
  color: var(--qts-text-dim);
}

@media (max-width: 1280px) {
  .trade-grid {
    grid-template-columns: 1fr;
  }

  .order-panel {
    position: static;
    order: -1;
  }
}

@media (max-width: 1024px) {
  .trade-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
