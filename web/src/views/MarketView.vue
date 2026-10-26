<template>
  <div class="page-stack">
    <PageHeader title="行情中心" subtitle="OKX 行情数据与 K 线图表">
      <template #tag>
        <el-tag size="small" effect="plain" :type="dataSource === 'synthetic' ? 'warning' : 'success'">
          {{ dataSourceLabel }}
        </el-tag>
      </template>
      <el-button :icon="Refresh" :loading="loading" @click="refreshAll">刷新</el-button>
      <el-button type="primary" :icon="Download" :loading="syncing" @click="syncData">同步全部品种</el-button>
    </PageHeader>

    <el-alert
      v-if="dataSource === 'synthetic'"
      type="warning"
      title="OKX 当前不可达，行情已自动切换为示例数据"
      description="示例数据由后端本地生成，用于在没有外网时演示图表与策略流程；恢复 OKX 连接后可重新同步真实行情。"
      show-icon
      :closable="false"
    />
    <el-alert v-if="error" type="error" :title="error" show-icon :closable="false" />

    <section class="qts-card market-toolbar">
      <div class="toolbar">
        <div class="toolbar-group">
          <span class="toolbar-label">交易对</span>
          <div class="pills">
            <button
              v-for="inst in instruments"
              :key="inst.instId"
              type="button"
              class="pill-btn"
              :class="{ active: instId === inst.instId }"
              @click="selectInstrument(inst.instId)"
            >
              {{ inst.base }}<span class="pill-btn-quote">/{{ inst.quote }}</span>
            </button>
          </div>
        </div>
        <div class="toolbar-group">
          <span class="toolbar-label">K 线周期</span>
          <div class="pills pills--compact">
            <button
              v-for="option in BAR_OPTIONS"
              :key="option.value"
              type="button"
              class="pill-btn"
              :class="{ active: bar === option.value }"
              @click="selectBar(option.value)"
            >
              {{ option.label }}
            </button>
          </div>
        </div>
      </div>
    </section>

    <section class="qts-card ticker-card">
      <div class="ticker-main">
        <div class="ticker-symbol">
          <span class="ticker-inst">{{ selectedInst.name || instId }}</span>
          <el-tag size="small" effect="plain" type="info">{{ instId }}</el-tag>
          <span class="text-dim ticker-bar">{{ barLabel(bar) }}</span>
        </div>
        <div class="ticker-price-row">
          <span class="ticker-price num" :class="tickerClass">{{ fmtPrice(ticker.last) }}</span>
          <span class="ticker-change num" :class="tickerClass">{{ fmtPct(ticker.change24hPct) }}</span>
        </div>
        <div class="ticker-updated">最近更新 {{ fmtDateTime(ticker.time) }}</div>
      </div>
      <div class="ticker-stats">
        <div class="ticker-stat">
          <span>24h 最高</span>
          <b class="num">{{ fmtPrice(ticker.high24h) }}</b>
        </div>
        <div class="ticker-stat">
          <span>24h 最低</span>
          <b class="num">{{ fmtPrice(ticker.low24h) }}</b>
        </div>
        <div class="ticker-stat">
          <span>24h 成交量</span>
          <b class="num">{{ fmtCompact(ticker.volume24h) }}</b>
        </div>
        <div class="ticker-stat">
          <span>K 线数量</span>
          <b class="num">{{ candles.length }} 根</b>
        </div>
      </div>
    </section>

    <section class="qts-card">
      <div class="card-head">
        <div class="card-title">{{ selectedInst.name || instId }} · {{ barLabel(bar) }} K 线</div>
        <div class="card-extra">
          <span class="chart-legend"><i class="legend-line legend-ma5"></i>MA5</span>
          <span class="chart-legend"><i class="legend-line legend-ma20"></i>MA20</span>
          <span class="chart-legend"><i class="legend-bar"></i>成交量</span>
        </div>
      </div>
      <div class="card-body card-body--chart">
        <div v-if="candlesLoading && !candles.length" v-loading="true" class="chart-loading"></div>
        <KlineChart v-else-if="candles.length" :candles="candles" :height="442" />
        <EmptyState
          v-else
          icon="TrendCharts"
          title="暂无 K 线数据"
          description="该交易对与周期暂时没有缓存数据，请先同步历史行情。"
        >
          <el-button type="primary" size="small" :loading="syncing" @click="syncData">同步全部品种近 30 天数据</el-button>
        </EmptyState>
      </div>
      <div class="card-foot market-foot">
        <span>数据来源：<b>{{ dataSourceLabel }}</b></span>
        <span>K 线时间：{{ candles.length ? fmtDateTime(candles[0].time) : '—' }} ~ {{ candles.length ? fmtDateTime(candles[candles.length - 1].time) : '—' }}</span>
        <span class="market-foot-tip">图表支持滚轮缩放与拖动平移</span>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Download, Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import KlineChart from '@/components/KlineChart.vue'
import EmptyState from '@/components/EmptyState.vue'
import { marketApi, settingsApi } from '@/api'
import { BAR_OPTIONS, DEFAULT_INSTRUMENTS, barLabel } from '@/utils/constants'
import { fmtCompact, fmtDateTime, fmtPct, fmtPrice } from '@/utils/format'

const instruments = ref(DEFAULT_INSTRUMENTS.map((item) => ({ ...item })))
const instId = ref('BTC-USDT')
const bar = ref('1H')
const ticker = ref({})
const candles = ref([])
const loading = ref(false)
const tickerLoading = ref(false)
const candlesLoading = ref(false)
const syncing = ref(false)
const error = ref('')
const dataSource = ref('okx')

let tickerTimer = null
let candleTimer = null

const selectedInst = computed(
  () => instruments.value.find((item) => item.instId === instId.value) || { name: instId.value }
)

const dataSourceLabel = computed(() =>
  dataSource.value === 'synthetic' ? '示例数据（OKX 不可达）' : 'OKX 实时行情'
)

const tickerClass = computed(() => {
  const value = Number(ticker.value.change24hPct ?? 0)
  if (value > 0) return 'is-profit'
  if (value < 0) return 'is-loss'
  return 'is-flat'
})

function sortCandles(list) {
  return [...list].sort((a, b) => Number(a.time) - Number(b.time))
}

async function loadInstruments() {
  try {
    const res = await marketApi.instruments()
    if (Array.isArray(res) && res.length) {
      instruments.value = res
      if (!res.some((item) => item.instId === instId.value)) {
        instId.value = res[0].instId
      }
    }
  } catch {
    // 保留默认交易对，行情接口失败时仍可展示界面
  }
}

async function loadSystemInfo() {
  try {
    const res = await settingsApi.system()
    if (res && res.okxReachable === false) dataSource.value = 'synthetic'
    else if (res && res.okxReachable === true) dataSource.value = 'okx'
  } catch {
    // 忽略系统状态读取失败
  }
}

async function loadTicker(silent = false) {
  if (!instId.value) return
  if (!silent) tickerLoading.value = true
  try {
    const res = await marketApi.ticker(instId.value)
    ticker.value = res || {}
  } catch (err) {
    if (!silent) error.value = err?.message || '行情快照加载失败'
  } finally {
    tickerLoading.value = false
  }
}

async function loadCandles(silent = false) {
  if (!instId.value) return
  if (!silent) candlesLoading.value = true
  try {
    const res = await marketApi.candles({ instId: instId.value, bar: bar.value, limit: 300 })
    candles.value = Array.isArray(res) ? sortCandles(res) : []
    error.value = ''
  } catch (err) {
    if (!silent) {
      candles.value = []
      error.value = err?.message || 'K 线数据加载失败'
    }
  } finally {
    candlesLoading.value = false
  }
}

async function refreshAll() {
  loading.value = true
  try {
    await Promise.all([loadTicker(), loadCandles()])
  } finally {
    loading.value = false
  }
}

function selectInstrument(value) {
  if (instId.value === value) return
  instId.value = value
  candles.value = []
  ticker.value = {}
  refreshAll()
}

function selectBar(value) {
  if (bar.value === value) return
  bar.value = value
  loadCandles()
}

async function syncData() {
  if (!instruments.value.length) return
  syncing.value = true
  try {
    const res = await marketApi.syncAll({ bar: bar.value, days: 30 })
    if (res?.source) dataSource.value = res.source
    ElMessage.success(res?.message || `已同步 ${res?.instrumentCount ?? 0} 个交易对`)
    await refreshAll()
  } catch (err) {
    ElMessage.error(err?.message || '同步历史数据失败')
  } finally {
    syncing.value = false
  }
}

function startAutoRefresh() {
  tickerTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    loadTicker(true)
  }, 15000)
  candleTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    loadCandles(true)
  }, 30000)
}

onMounted(async () => {
  await Promise.all([loadInstruments(), loadSystemInfo()])
  await refreshAll()
  startAutoRefresh()
})

onBeforeUnmount(() => {
  if (tickerTimer) clearInterval(tickerTimer)
  if (candleTimer) clearInterval(candleTimer)
})
</script>

<style scoped>
.market-toolbar {
  padding: 14px 18px;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 18px 34px;
  flex-wrap: wrap;
}

.toolbar-group {
  display: flex;
  align-items: center;
  gap: 11px;
  flex-wrap: wrap;
  min-width: 0;
}

.toolbar-label {
  font-size: 11px;
  letter-spacing: 1px;
  color: var(--qts-text-dim);
  white-space: nowrap;
}

.pills {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.pill-btn {
  height: 31px;
  padding: 0 14px;
  border-radius: 9px;
  border: 1px solid rgba(148, 163, 184, 0.14);
  background: rgba(148, 163, 184, 0.04);
  color: var(--qts-text-muted);
  font-size: 12.5px;
  font-weight: 600;
  letter-spacing: 0.2px;
  cursor: pointer;
  transition: all 0.18s ease;
  font-family: inherit;
}

.pill-btn-quote {
  color: var(--qts-text-dim);
  font-weight: 500;
}

.pill-btn:hover {
  color: #e2e8f0;
  border-color: rgba(34, 211, 238, 0.32);
}

.pill-btn.active {
  color: #67e8f9;
  border-color: rgba(34, 211, 238, 0.5);
  background: rgba(34, 211, 238, 0.1);
  box-shadow: 0 0 16px rgba(34, 211, 238, 0.12);
}

.pill-btn.active .pill-btn-quote {
  color: rgba(103, 232, 249, 0.75);
}

.pills--compact .pill-btn {
  padding: 0 12px;
  height: 29px;
  font-size: 12px;
}

/* 行情快照 */
.ticker-card {
  display: flex;
  align-items: stretch;
  justify-content: space-between;
  gap: 20px;
  padding: 20px 24px;
  flex-wrap: wrap;
}

.ticker-main {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.ticker-symbol {
  display: flex;
  align-items: center;
  gap: 9px;
}

.ticker-inst {
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 0.4px;
  color: #f1f5f9;
}

.ticker-bar {
  font-size: 11.5px;
}

.ticker-price-row {
  display: flex;
  align-items: baseline;
  gap: 14px;
}

.ticker-price {
  font-size: 38px;
  font-weight: 700;
  letter-spacing: 0.5px;
  line-height: 1.1;
}

.ticker-price.is-profit {
  color: #4ade80;
}

.ticker-price.is-loss {
  color: #fb7185;
}

.ticker-price.is-flat {
  color: #e2e8f0;
}

.ticker-change {
  font-size: 14px;
  font-weight: 600;
  padding: 2px 9px;
  border-radius: 7px;
  background: rgba(148, 163, 184, 0.08);
}

.ticker-change.is-profit {
  color: #4ade80;
  background: rgba(52, 211, 153, 0.1);
}

.ticker-change.is-loss {
  color: #fb7185;
  background: rgba(251, 113, 133, 0.1);
}

.ticker-updated {
  font-size: 11px;
  color: var(--qts-text-dim);
}

.ticker-stats {
  display: grid;
  grid-template-columns: repeat(4, minmax(96px, auto));
  gap: 12px 34px;
  align-items: center;
  padding-left: 22px;
  border-left: 1px solid rgba(148, 163, 184, 0.09);
}

.ticker-stat {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ticker-stat span {
  font-size: 11px;
  color: var(--qts-text-dim);
}

.ticker-stat b {
  font-size: 14px;
  font-weight: 600;
  color: var(--qts-text);
}

/* 图表区 */
.chart-loading {
  height: 442px;
  border-radius: 12px;
  border: 1px dashed rgba(148, 163, 184, 0.12);
  background: rgba(148, 163, 184, 0.025);
}

.chart-legend {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11px;
  color: var(--qts-text-dim);
  margin-left: 10px;
}

.legend-line {
  display: inline-block;
  width: 14px;
  height: 2px;
  border-radius: 2px;
}

.legend-ma5 {
  background: #fbbf24;
}

.legend-ma20 {
  background: #a78bfa;
}

.legend-bar {
  display: inline-block;
  width: 5px;
  height: 9px;
  border-radius: 1px;
  background: rgba(34, 211, 238, 0.6);
}

.market-foot {
  flex-wrap: wrap;
  gap: 6px 22px;
}

.market-foot b {
  color: #cbd5e1;
  font-weight: 600;
}

.market-foot-tip {
  margin-left: auto;
  color: rgba(100, 116, 139, 0.85);
}

@media (max-width: 960px) {
  .ticker-stats {
    grid-template-columns: repeat(2, minmax(110px, 1fr));
    gap: 12px 24px;
    padding-left: 0;
    padding-top: 14px;
    border-left: none;
    border-top: 1px solid rgba(148, 163, 184, 0.09);
    width: 100%;
  }

  .ticker-price {
    font-size: 32px;
  }
}

@media (max-width: 640px) {
  .ticker-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .market-foot-tip {
    margin-left: 0;
    width: 100%;
  }
}
</style>
