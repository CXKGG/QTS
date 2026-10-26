<template>
  <div class="page-stack">
    <PageHeader title="AI 决策" subtitle="走势分析 · 策略生成 · 一键回测 · 模拟盘验证">
      <template #tag>
        <span
          class="engine-badge"
          :class="{ 'is-llm': isLlm, 'is-loading': statusLoading }"
          :title="engineTitle"
        >
          <i class="engine-dot" />
          {{ engineLabel }}
        </span>
        <span v-if="engineModel" class="engine-model text-dim">{{ engineModel }}</span>
      </template>
      <el-button :icon="Refresh" :loading="statusLoading" @click="loadStatus">刷新引擎状态</el-button>
    </PageHeader>

    <el-alert
      v-if="analysisError"
      :title="analysisError"
      type="error"
      show-icon
      :closable="false"
      class="page-alert"
    />

    <section class="qts-card">
      <div class="card-head">
        <span class="card-title">分析参数</span>
        <span class="card-extra">{{ statusMessage }}</span>
      </div>
      <div class="card-body">
        <div class="control-row">
          <div class="ctrl-item">
            <span class="ctrl-label">交易对</span>
            <el-select v-model="form.instId" style="width: 100%">
              <el-option
                v-for="item in instruments"
                :key="item.instId"
                :label="item.name || item.instId"
                :value="item.instId"
              />
            </el-select>
          </div>
          <div class="ctrl-item">
            <span class="ctrl-label">分析周期</span>
            <el-select v-model="form.bar" style="width: 100%">
              <el-option v-for="item in AI_BARS" :key="item.value" :label="item.label" :value="item.value" />
            </el-select>
          </div>
          <div class="ctrl-item">
            <span class="ctrl-label">风险偏好</span>
            <el-select v-model="form.riskProfile" style="width: 100%">
              <el-option v-for="item in RISK_OPTIONS" :key="item.value" :label="item.label" :value="item.value">
                <span>{{ item.label }}</span>
                <span class="option-hint">{{ item.hint }}</span>
              </el-option>
            </el-select>
          </div>
          <div class="ctrl-item">
            <span class="ctrl-label">回看天数</span>
            <el-input-number
              v-model="form.lookbackDays"
              :min="30"
              :max="365"
              :step="15"
              :precision="0"
              controls-position="right"
              style="width: 100%"
            />
          </div>
          <div class="ctrl-item ctrl-item--switch">
            <span class="ctrl-label">允许做空</span>
            <el-switch v-model="form.allowShort" inline-prompt active-text="是" inactive-text="否" />
          </div>
          <div class="ctrl-action">
            <el-button type="primary" :icon="MagicStick" :loading="analyzing" @click="analyze">
              {{ analyzing ? 'AI 分析中…' : '开始 AI 分析' }}
            </el-button>
          </div>
        </div>
        <p class="control-hint">
          <el-icon><InfoFilled /></el-icon>
          分析将读取 {{ instName }} 最近 {{ form.lookbackDays }} 天 {{ barLabel(form.bar) }} K 线，结合趋势、均线、RSI、MACD、波动率等指标生成策略方案；AI 结果仅供研究，请先回测验证。
        </p>
      </div>
    </section>

    <div class="ai-layout">
      <div class="ai-main">
        <section class="qts-card chart-card">
          <div class="card-head chart-head">
            <div class="chart-head-main">
              <span class="card-title">{{ instName }} · 走势</span>
              <span class="text-dim">{{ barLabel(form.bar) }} · 图表 {{ candles.length }} 根</span>
            </div>
            <div class="chart-head-side">
              <span v-if="hasValue(chartLastPrice)" class="num chart-price">{{ fmtPrice(chartLastPrice) }}</span>
              <span v-if="chartChangePct !== null" class="num chart-change" :class="pnlClass(chartChangePct)">
                {{ fmtPct(chartChangePct, 2) }}
              </span>
              <el-button link :icon="Refresh" :loading="candlesLoading" @click="loadCandles">刷新图表</el-button>
            </div>
          </div>
          <div class="card-body card-body--flush chart-body">
            <div v-if="dataSourceSynthetic" class="chart-warning">
              <el-alert
                :title="analysis?.warning || '当前行情为示例数据（OKX 不可达），AI 分析与回测结果仅用于演示'"
                type="warning"
                show-icon
                :closable="false"
              />
            </div>
            <div v-if="candlesLoading && !candles.length" class="chart-loading">
              <el-skeleton :rows="6" animated />
            </div>
            <KlineChart v-else-if="candles.length" :candles="candles" :height="300" />
            <EmptyState
              v-else
              icon="TrendCharts"
              :title="chartError || '暂无 K 线数据'"
              description="确认后端行情接口可用后刷新；离线时后端会返回示例数据"
            >
              <el-button :icon="Refresh" @click="loadCandles">重新加载</el-button>
            </EmptyState>
          </div>
        </section>

        <section v-if="analysis" class="qts-card">
          <div class="card-head">
            <span class="card-title">技术指标</span>
            <span class="card-extra">
              K 线 {{ analysis.candles }} 根 · 分析于 {{ fmtRelative(analysis.generatedAt) }}
            </span>
          </div>
          <div class="card-body">
            <div class="ind-grid">
              <div class="ind-tile ind-tile--wide">
                <span class="ind-label">趋势方向 / 强度</span>
                <div class="ind-value-row">
                  <el-tag size="small" effect="plain" :type="trendMeta.type">{{ trendMeta.label }}</el-tag>
                  <span class="ind-value num" :class="trendScoreClass">{{ fmtNumber(trend.score, 0) }}</span>
                  <span class="ind-hint">趋势评分（-100 ~ 100）</span>
                </div>
                <el-progress
                  :percentage="clampPct(trend.strength)"
                  :show-text="false"
                  :stroke-width="6"
                  :color="trendColor"
                  class="ind-bar"
                />
                <span class="ind-hint">趋势强度 {{ fmtNumber(trend.strength, 0) }} / 100</span>
              </div>
              <div class="ind-tile">
                <span class="ind-label">EMA20</span>
                <span class="ind-value num">{{ fmtPrice(indicators.ema20) }}</span>
                <span class="ind-hint">{{ emaHint }}</span>
              </div>
              <div class="ind-tile">
                <span class="ind-label">EMA60</span>
                <span class="ind-value num">{{ fmtPrice(indicators.ema60) }}</span>
                <span class="ind-hint">中期均线</span>
              </div>
              <div class="ind-tile">
                <span class="ind-label">RSI14</span>
                <span class="ind-value num">{{ fmtNumber(indicators.rsi14, 2) }}</span>
                <span class="ind-hint">{{ rsiHint }}</span>
              </div>
              <div class="ind-tile">
                <span class="ind-label">MACD</span>
                <span class="ind-value num">{{ fmtNumber(indicators.macd, 4) }}</span>
                <span class="ind-hint">信号线 {{ fmtNumber(indicators.macdSignal, 4) }} · {{ macdHint }}</span>
              </div>
              <div class="ind-tile">
                <span class="ind-label">ATR 波动率</span>
                <span class="ind-value num">{{ fmtPct(indicators.atrPct, 2, false) }}</span>
                <span class="ind-hint">{{ atrHint }}</span>
              </div>
              <div class="ind-tile">
                <span class="ind-label">量比</span>
                <span class="ind-value num">{{ fmtNumber(indicators.volumeRatio, 2) }}</span>
                <span class="ind-hint">{{ volumeHint }}</span>
              </div>
              <div class="ind-tile ind-tile--wide">
                <span class="ind-label">支撑位</span>
                <span class="ind-value num">{{ fmtPrice(indicators.support) }}</span>
                <span class="ind-hint">{{ supportHint }}</span>
              </div>
              <div class="ind-tile ind-tile--wide">
                <span class="ind-label">阻力位</span>
                <span class="ind-value num">{{ fmtPrice(indicators.resistance) }}</span>
                <span class="ind-hint">{{ resistanceHint }}</span>
              </div>
            </div>
          </div>
        </section>
      </div>

      <aside class="ai-side">
        <section class="qts-card sug-card">
          <div class="card-head">
            <span class="card-title">AI 策略方案</span>
            <el-tag v-if="analyzing" size="small" type="warning" effect="plain">分析中</el-tag>
            <el-tag v-else-if="analysisStale" size="small" type="warning" effect="plain">参数已变更，请重新分析</el-tag>
            <el-tag v-else-if="analysis" size="small" type="success" effect="plain">已生成</el-tag>
          </div>

          <div v-if="analyzing" class="card-body">
            <div class="analyzing-box">
              <el-icon class="is-loading"><Loading /></el-icon>
              <div>
                <b>AI 正在分析走势…</b>
                <p>读取行情、计算指标并生成策略参数，预计数秒。</p>
              </div>
            </div>
            <el-skeleton :rows="5" animated />
          </div>

          <div v-else-if="analysis" class="card-body">
            <div class="sug-hero">
              <div class="sug-title-row">
                <div class="sug-title-main">
                  <span class="sug-name">{{ suggestion.strategyName || suggestion.strategyCode || '策略方案' }}</span>
                  <div class="sug-sub">
                    <code class="code-pill">{{ suggestion.strategyCode || '—' }}</code>
                    <el-tag size="small" effect="plain" :type="biasMeta.type">{{ biasMeta.label }}</el-tag>
                  </div>
                </div>
                <div class="conf-badge" :style="{ color: confidenceColor }">
                  {{ fmtNumber(confidence, 0) }}<span class="conf-unit">%</span>
                  <small>置信度</small>
                </div>
              </div>
              <div class="conf-row">
                <span class="conf-label">置信度</span>
                <div class="conf-track">
                  <div
                    class="conf-fill"
                    :style="{ width: `${confidence}%`, background: confidenceColor }"
                  />
                </div>
                <span class="num conf-value">{{ fmtNumber(confidence, 0) }}%</span>
              </div>
            </div>

            <p class="sug-summary">{{ suggestion.summary || 'AI 未返回方案摘要。' }}</p>

            <div class="sug-section">
              <div class="sug-section-title">策略参数</div>
              <div class="chip-row">
                <el-tag
                  v-for="chip in paramChips"
                  :key="chip.key"
                  size="small"
                  effect="plain"
                  class="param-chip"
                >
                  {{ chip.label }} {{ chip.value }}
                </el-tag>
                <span v-if="!paramChips.length" class="text-dim">使用策略默认参数</span>
              </div>
            </div>

            <div class="sug-section">
              <div class="sug-section-title">风险与资金</div>
              <div class="risk-grid">
                <div class="risk-tile">
                  <span>杠杆</span>
                  <b class="num">{{ Math.max(1, Number(suggestion.leverage || 1)) }}×</b>
                </div>
                <div class="risk-tile">
                  <span>滑点</span>
                  <b class="num">{{ fmtPct(suggestion.slippagePct, 2, false) }}</b>
                </div>
                <div class="risk-tile">
                  <span>止损</span>
                  <b class="num">{{ fmtPct(risk.stopLossPct, 2, false) }}</b>
                </div>
                <div class="risk-tile">
                  <span>止盈</span>
                  <b class="num">{{ fmtPct(risk.takeProfitPct, 2, false) }}</b>
                </div>
                <div class="risk-tile">
                  <span>最大仓位</span>
                  <b class="num">{{ fmtPct(risk.maxPositionPct, 2, false) }}</b>
                </div>
                <div class="risk-tile">
                  <span>资金占比</span>
                  <b class="num">{{ fmtPct(suggestion.capitalPct, 2, false) }}</b>
                  <small>≈ {{ fmtMoney(allocatedCapital) }} USDT</small>
                </div>
              </div>
            </div>

            <div v-if="rationale.length" class="sug-section">
              <div class="sug-section-title">分析理由</div>
              <ul class="reason-list">
                <li v-for="(item, index) in rationale" :key="`reason-${index}`">
                  <el-icon><CircleCheck /></el-icon>
                  <span>{{ item }}</span>
                </li>
              </ul>
            </div>

            <div v-if="warnings.length" class="sug-section">
              <div class="sug-section-title">模型风险提示</div>
              <ul class="warn-list">
                <li v-for="(item, index) in warnings" :key="`warn-${index}`">
                  <el-icon><Warning /></el-icon>
                  <span>{{ item }}</span>
                </li>
              </ul>
            </div>

            <el-alert
              class="ai-risk"
              type="warning"
              show-icon
              :closable="false"
              title="AI 生成方案仅供研究，不构成投资建议，请先回测再在模拟盘验证。"
            />

            <div class="sug-actions">
              <el-button type="primary" :loading="backtesting" :disabled="analyzing" @click="runBacktest">
                一键回测
              </el-button>
              <el-button type="success" plain :loading="taskCreating" :disabled="analyzing" @click="createTask">
                创建模拟任务
              </el-button>
              <el-button plain :icon="Refresh" :loading="analyzing" @click="analyze">重新分析</el-button>
            </div>

            <p class="sug-meta text-dim">
              生成于 {{ fmtDateTime(analysis.generatedAt) }} · {{ analysis.provider || '—' }} / {{ analysis.model || '—' }} ·
              数据源 {{ dataSourceLabel }} · 现价 {{ fmtPrice(analysis.lastPrice) }}
            </p>
          </div>

          <div v-else class="card-body">
            <EmptyState
              icon="MagicStick"
              title="尚未生成策略方案"
              description="选择交易对、周期与风险偏好，点击“开始 AI 分析”生成方案"
            >
              <el-button type="primary" :icon="MagicStick" :loading="analyzing" @click="analyze">
                开始 AI 分析
              </el-button>
            </EmptyState>
          </div>
        </section>

        <section v-if="backtesting || backtestResult || backtestError" class="qts-card">
          <div class="card-head">
            <span class="card-title">AI 策略回测</span>
            <el-tag v-if="backtestResult" size="small" effect="plain" :type="backtestStatusMeta.type">
              {{ backtestStatusMeta.label }}
            </el-tag>
          </div>
          <div class="card-body">
            <div v-if="backtesting" class="bt-loading">
              <el-icon class="is-loading"><Loading /></el-icon>
              <span>正在使用最近 365 天数据回测，可能需要数秒…</span>
            </div>

            <el-alert
              v-if="backtestError"
              :title="backtestError"
              type="error"
              show-icon
              :closable="false"
              class="bt-error"
            />

            <template v-if="backtestResult">
              <div class="bt-grid">
                <div class="bt-tile">
                  <span>总收益率</span>
                  <b class="num" :class="pnlClass(btSummary.totalReturnPct)">
                    {{ fmtPct(btSummary.totalReturnPct, 2) }}
                  </b>
                </div>
                <div class="bt-tile">
                  <span>最大回撤</span>
                  <b class="num is-loss">{{ fmtPct(btSummary.maxDrawdownPct, 2, false) }}</b>
                </div>
                <div class="bt-tile">
                  <span>夏普比率</span>
                  <b class="num">{{ fmtNumber(btSummary.sharpe, 2) }}</b>
                </div>
                <div class="bt-tile">
                  <span>交易次数</span>
                  <b class="num">{{ fmtNumber(btSummary.tradeCount, 0) }}</b>
                </div>
              </div>
              <div class="bt-meta">
                <span v-if="backtestResult.id" class="num">#{{ backtestResult.id }}</span>
                <el-tag size="small" effect="plain">
                  数据源 {{ backtestResult.dataSource === 'synthetic' ? '示例数据' : 'OKX 缓存' }}
                </el-tag>
                <span>最终权益 {{ fmtMoney(btSummary.finalEquity) }} USDT</span>
                <span>胜率 {{ fmtPct(btSummary.winRatePct, 2, false) }}</span>
              </div>
              <p v-if="backtestMessage" class="bt-message text-dim">{{ backtestMessage }}</p>
              <el-button link type="primary" @click="router.push('/backtest')">
                前往回测页查看历史回测 →
              </el-button>
            </template>
          </div>
        </section>
      </aside>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MagicStick, Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import EmptyState from '@/components/EmptyState.vue'
import KlineChart from '@/components/KlineChart.vue'
import { aiApi, backtestApi, dashboardApi, marketApi, taskApi } from '@/api'
import {
  fmtDateTime,
  fmtMoney,
  fmtNumber,
  fmtPct,
  fmtPrice,
  fmtRelative,
  hasValue,
  pnlClass
} from '@/utils/format'
import { DEFAULT_INSTRUMENTS, barLabel } from '@/utils/constants'

const router = useRouter()

const AI_BARS = [
  { label: '1小时', value: '1H' },
  { label: '4小时', value: '4H' },
  { label: '1天', value: '1D' }
]

const RISK_OPTIONS = [
  { label: '稳健', value: 'conservative', hint: '低杠杆 · 低仓位' },
  { label: '平衡', value: 'balanced', hint: '中等杠杆与仓位' },
  { label: '激进', value: 'aggressive', hint: '高杠杆 · 高仓位' }
]

const BIAS_META = {
  long: { label: '偏多', type: 'success' },
  short: { label: '偏空', type: 'danger' },
  both: { label: '双向', type: 'warning' }
}

const PARAM_LABELS = {
  fastPeriod: '快线周期',
  slowPeriod: '慢线周期',
  rsiPeriod: 'RSI 周期',
  oversold: '超卖阈值',
  overbought: '超买阈值',
  bollPeriod: '布林周期',
  bollDev: '布林倍数',
  macdFast: 'MACD 快线',
  macdSlow: 'MACD 慢线',
  macdSignal: 'MACD 信号',
  ignoreFlat: '忽略震荡市',
  allowShort: '允许做空'
}

const form = reactive({
  instId: 'BTC-USDT',
  bar: '1D',
  riskProfile: 'balanced',
  lookbackDays: 180,
  allowShort: true
})

const statusLoading = ref(false)
const engine = ref(null)
const statusError = ref('')

const instruments = ref([...DEFAULT_INSTRUMENTS])
const candles = ref([])
const candlesLoading = ref(false)
const chartError = ref('')

const analyzing = ref(false)
const analysis = ref(null)
const analysisError = ref('')

const backtesting = ref(false)
const backtestResult = ref(null)
const backtestSummary = ref(null)
const backtestMessage = ref('')
const backtestError = ref('')

const taskCreating = ref(false)
const accountEquity = ref(100)

const isLlm = computed(() => engine.value?.mode === 'llm')
const engineLabel = computed(() => {
  if (statusLoading.value && !engine.value) return '检测引擎中…'
  if (statusError.value) return '本地引擎'
  return isLlm.value ? '大模型引擎' : '本地引擎'
})
const engineModel = computed(() => {
  if (!engine.value) return ''
  return [engine.value.provider, engine.value.model].filter(Boolean).join(' / ')
})
const engineTitle = computed(() => engine.value?.message || statusError.value || '')
const statusMessage = computed(() => {
  if (statusError.value) return statusError.value
  if (!engine.value) return '正在检测 AI 引擎状态…'
  if (engine.value.message) return engine.value.message
  return isLlm.value ? '已接入大模型，分析结论由模型生成' : '当前使用本地规则引擎，无需外部模型'
})

const instName = computed(
  () => instruments.value.find((item) => item.instId === form.instId)?.name || form.instId
)

const trend = computed(() => analysis.value?.trend || {})
const indicators = computed(() => analysis.value?.indicators || {})
const suggestion = computed(() => analysis.value?.suggestion || {})
const risk = computed(() => suggestion.value?.risk || {})
const params = computed(() => suggestion.value?.params || {})
const rationale = computed(() =>
  Array.isArray(suggestion.value?.rationale) ? suggestion.value.rationale : []
)
const warnings = computed(() =>
  Array.isArray(suggestion.value?.warnings) ? suggestion.value.warnings : []
)

const analysisStale = computed(() => {
  if (!analysis.value) return false
  return String(analysis.value.instId || '') !== form.instId || String(analysis.value.bar || '') !== form.bar
})

const confidence = computed(() => clampPct(suggestion.value?.confidence))
const trendMeta = computed(() => {
  const direction = String(trend.value.direction || '')
  if (direction === 'up') return { label: trend.value.label || '上升趋势', type: 'success' }
  if (direction === 'down') return { label: trend.value.label || '下降趋势', type: 'danger' }
  return { label: trend.value.label || '震荡整理', type: 'info' }
})
const trendColor = computed(() => {
  const direction = String(trend.value.direction || '')
  if (direction === 'up') return '#34d399'
  if (direction === 'down') return '#fb7185'
  return '#fbbf24'
})
const trendScoreClass = computed(() => {
  const direction = String(trend.value.direction || '')
  if (direction === 'up') return 'is-profit'
  if (direction === 'down') return 'is-loss'
  return 'is-flat'
})
const biasMeta = computed(() => {
  const bias = String(suggestion.value?.directionBias || '').toLowerCase()
  return BIAS_META[bias] || { label: '中性', type: 'info' }
})
const confidenceColor = computed(() => {
  const value = confidence.value
  if (value >= 70) return '#34d399'
  if (value >= 50) return '#22d3ee'
  if (value >= 30) return '#fbbf24'
  return '#fb7185'
})

const emaHint = computed(() => {
  const ema20 = Number(indicators.value.ema20)
  const ema60 = Number(indicators.value.ema60)
  if (!Number.isFinite(ema20) || !Number.isFinite(ema60)) return '—'
  return ema20 >= ema60 ? '快线在慢线上方 · 多头排列' : '快线在慢线下方 · 空头排列'
})
const rsiHint = computed(() => {
  const value = Number(indicators.value.rsi14)
  if (!Number.isFinite(value)) return '—'
  if (value >= 70) return '超买区，谨防回调'
  if (value <= 30) return '超卖区，关注反弹'
  return '中性区间'
})
const macdHint = computed(() => {
  const macd = Number(indicators.value.macd)
  const signal = Number(indicators.value.macdSignal)
  if (!Number.isFinite(macd) || !Number.isFinite(signal)) return '—'
  return macd >= signal ? '位于信号线上方' : '位于信号线下方'
})
const atrHint = computed(() => {
  const value = Number(indicators.value.atrPct)
  if (!Number.isFinite(value)) return '—'
  if (value >= 3) return '波动偏高'
  if (value <= 1) return '波动偏低'
  return '波动适中'
})
const volumeHint = computed(() => {
  const value = Number(indicators.value.volumeRatio)
  if (!Number.isFinite(value)) return '—'
  if (value >= 1.5) return '明显放量'
  if (value >= 1) return '温和放量'
  return '缩量'
})
const supportHint = computed(() => {
  const last = Number(analysis.value?.lastPrice)
  const support = Number(indicators.value.support)
  if (!Number.isFinite(last) || !Number.isFinite(support) || !last) return '短期支撑参考'
  if (last <= support) return '现价已跌破支撑'
  return `距支撑 ${fmtPct(((last - support) / last) * 100, 2, false)}`
})
const resistanceHint = computed(() => {
  const last = Number(analysis.value?.lastPrice)
  const resistance = Number(indicators.value.resistance)
  if (!Number.isFinite(last) || !Number.isFinite(resistance) || !last) return '短期阻力参考'
  if (last >= resistance) return '现价已突破阻力'
  return `距阻力 ${fmtPct(((resistance - last) / last) * 100, 2, false)}`
})

const paramChips = computed(() =>
  Object.entries(params.value).map(([key, value]) => ({
    key,
    label: PARAM_LABELS[key] || key,
    value: fmtParam(value)
  }))
)

const dataSourceSynthetic = computed(() => analysis.value?.dataSource === 'synthetic')
const dataSourceLabel = computed(() => {
  const source = String(analysis.value?.dataSource || '')
  if (source === 'okx') return 'OKX 实时'
  if (source === 'synthetic') return '示例数据'
  return '—'
})

const chartLastPrice = computed(() => {
  if (hasValue(analysis.value?.lastPrice)) return analysis.value.lastPrice
  const last = candles.value[candles.value.length - 1]
  return last ? last.close : null
})
const chartChangePct = computed(() => {
  if (hasValue(analysis.value?.changePct)) return Number(analysis.value.changePct)
  const list = candles.value
  if (list.length < 2) return null
  const first = Number(list[list.length - 2]?.close)
  const last = Number(list[list.length - 1]?.close)
  if (!Number.isFinite(first) || !Number.isFinite(last) || !first) return null
  return ((last - first) / first) * 100
})

const allocatedCapital = computed(() => {
  const pct = Number(suggestion.value?.capitalPct)
  const base = Number(accountEquity.value) || 100
  const safePct = Number.isFinite(pct) ? Math.min(100, Math.max(1, pct)) : 20
  return Math.max(1, Number(((base * safePct) / 100).toFixed(2)))
})

const backtestStatusMeta = computed(() => {
  const status = String(backtestResult.value?.status || '').toLowerCase()
  if (status === 'completed' || status === 'success') return { label: '已完成', type: 'success' }
  if (status === 'failed' || status === 'error') return { label: '失败', type: 'danger' }
  if (status) return { label: '执行中', type: 'warning' }
  return { label: '回测中', type: 'info' }
})
const btSummary = computed(() => backtestSummary.value || {})

function clampPct(value) {
  const num = Number(value)
  if (!Number.isFinite(num)) return 0
  return Math.max(0, Math.min(100, num))
}

function fmtParam(value) {
  if (typeof value === 'boolean') return value ? '是' : '否'
  if (value === null || value === undefined || value === '') return '—'
  return String(value)
}

async function loadStatus() {
  statusLoading.value = true
  statusError.value = ''
  try {
    engine.value = await aiApi.status()
  } catch (err) {
    engine.value = null
    statusError.value = err?.message || 'AI 引擎状态不可用，默认使用本地引擎'
  } finally {
    statusLoading.value = false
  }
}

async function loadInstruments() {
  try {
    const res = await marketApi.instruments()
    if (Array.isArray(res) && res.length) {
      instruments.value = res
      if (!res.some((item) => item.instId === form.instId)) {
        form.instId = res[0].instId
      }
    }
  } catch {
    instruments.value = [...DEFAULT_INSTRUMENTS]
  }
}

async function loadCandles() {
  candlesLoading.value = true
  try {
    const res = await marketApi.candles({ instId: form.instId, bar: form.bar, limit: 200 })
    candles.value = Array.isArray(res) ? [...res].sort((a, b) => Number(a.time) - Number(b.time)) : []
    chartError.value = ''
  } catch (err) {
    candles.value = []
    chartError.value = err?.message || 'K 线数据加载失败'
  } finally {
    candlesLoading.value = false
  }
}

async function loadAccountEquity() {
  try {
    const data = await dashboardApi.overview()
    const equity = data?.live?.connected
      ? Number(data.live.availableUsdt)
      : Number(data?.paper?.equity ?? data?.paper?.initialBalance)
    accountEquity.value = Number.isFinite(equity) && equity > 0 ? equity : 100
  } catch {
    accountEquity.value = 100
  }
}

async function analyze() {
  if (analyzing.value) return
  const lookback = Number(form.lookbackDays)
  if (!Number.isFinite(lookback) || lookback < 30 || lookback > 365) {
    analysisError.value = '回看天数需在 30–365 天之间'
    return
  }
  analyzing.value = true
  analysisError.value = ''
  backtestResult.value = null
  backtestSummary.value = null
  backtestMessage.value = ''
  backtestError.value = ''
  try {
    const res = await aiApi.analyze({
      instId: form.instId,
      bar: form.bar,
      riskProfile: form.riskProfile,
      lookbackDays: Math.round(lookback),
      allowShort: Boolean(form.allowShort)
    })
    analysis.value = res || null
    if (!analysis.value) {
      analysisError.value = 'AI 未返回分析结果，请稍后重试'
      return
    }
    await loadCandles()
  } catch (err) {
    analysis.value = null
    analysisError.value = err?.message || 'AI 分析失败，请稍后重试'
    ElMessage.error(analysisError.value)
  } finally {
    analyzing.value = false
  }
}

async function runBacktest() {
  if (!analysis.value || backtesting.value) return
  const current = analysis.value
  const advice = current.suggestion || {}
  const adviceRisk = advice.risk || {}
  const now = Date.now()
  backtesting.value = true
  backtestError.value = ''
  backtestResult.value = null
  backtestSummary.value = null
  backtestMessage.value = ''
  try {
    const created = await backtestApi.create({
      name: `AI · ${advice.strategyName || advice.strategyCode || '策略'} ${current.instId} ${barLabel(current.bar)}`,
      strategyCode: advice.strategyCode,
      instId: current.instId,
      bar: current.bar,
      startTime: new Date(now - 365 * 24 * 60 * 60 * 1000).toISOString(),
      endTime: new Date(now).toISOString(),
      initialCapital: 100,
      feeRatePct: 0.1,
      slippagePct: Number(advice.slippagePct ?? 0.05),
      positionSizePct: Number(adviceRisk.maxPositionPct ?? 30),
      leverage: Math.max(1, Number(advice.leverage || 1)),
      stopLossPct: Number(adviceRisk.stopLossPct ?? 0),
      takeProfitPct: Number(adviceRisk.takeProfitPct ?? 0),
      params: advice.params || {},
      preferLocalData: true
    })
    backtestMessage.value = created?.message || '回测已完成'
    const result = {
      id: created?.id ?? null,
      status: created?.status || 'completed',
      dataSource: current.dataSource
    }
    backtestResult.value = result
    backtestSummary.value = created?.metrics || null
    if (created?.id) {
      try {
        const detail = await backtestApi.detail(created.id)
        if (detail?.summary) {
          backtestSummary.value = detail.summary
          result.status = detail.summary.status || result.status
          result.dataSource = detail.summary.dataSource || result.dataSource
        } else if (detail?.dataSource) {
          result.dataSource = detail.dataSource
        }
      } catch {
        /* 保留创建接口返回的 metrics */
      }
    }
    if (!backtestSummary.value) backtestSummary.value = {}
    ElMessage.success('AI 策略回测完成')
  } catch (err) {
    backtestError.value = err?.message || '回测执行失败'
    ElMessage.error(backtestError.value)
  } finally {
    backtesting.value = false
  }
}

async function createTask() {
  if (!analysis.value || taskCreating.value) return
  const current = analysis.value
  const advice = current.suggestion || {}
  const adviceRisk = advice.risk || {}
  const capital = allocatedCapital.value
  const directionLabel = biasMeta.value.label
  try {
    await ElMessageBox.confirm(
      `将以 OKX API 账户创建实盘任务「AI · ${advice.strategyName || advice.strategyCode}」，交易对 ${current.instId}，周期 ${barLabel(current.bar)}，方向偏好 ${directionLabel}，分配资金 ${fmtMoney(capital)} USDT，杠杆 ${Math.max(1, Number(advice.leverage || 1))}×，并立即启动。\n\nAI 方案仅供研究参考，请确认已了解策略与实盘合约风险。`,
      '确认创建 API 实盘任务',
      {
        type: 'warning',
        confirmButtonText: '创建并启动',
        cancelButtonText: '取消',
        distinguishCancelAndClose: true
      }
    )
  } catch {
    return
  }
  taskCreating.value = true
  try {
    const created = await taskApi.create({
      name: `AI · ${advice.strategyName || advice.strategyCode || '策略'} ${current.instId} ${barLabel(current.bar)}`,
      strategyCode: advice.strategyCode,
      instId: current.instId,
      bar: current.bar,
      mode: 'Live',
      allocatedCapital: capital,
      leverage: Math.max(1, Number(advice.leverage || 1)),
      slippagePct: Number(advice.slippagePct ?? 0.05),
      params: advice.params || {},
      risk: {
        stopLossPct: Number(adviceRisk.stopLossPct ?? 0),
        takeProfitPct: Number(adviceRisk.takeProfitPct ?? 0),
        maxPositionPct: Number(adviceRisk.maxPositionPct ?? 0)
      },
      startImmediately: true
    })
    ElMessage.success(`实盘任务「${created?.name || 'AI 策略任务'}」已创建并启动`)
    await router.push('/tasks')
  } catch (err) {
    ElMessage.error(err?.message || '创建模拟任务失败')
  } finally {
    taskCreating.value = false
  }
}

watch(
  [() => form.instId, () => form.bar],
  () => {
    loadCandles()
  }
)

onMounted(() => {
  loadStatus()
  loadInstruments()
  loadAccountEquity()
  loadCandles()
})
</script>

<style scoped>
.page-alert {
  margin-bottom: 2px;
}

.engine-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px 10px;
  border-radius: 999px;
  border: 1px solid rgba(96, 165, 250, 0.32);
  background: rgba(96, 165, 250, 0.1);
  color: #93c5fd;
  font-size: 11.5px;
  line-height: 1.4;
  white-space: nowrap;
}

.engine-badge.is-llm {
  border-color: rgba(167, 139, 250, 0.36);
  background: rgba(167, 139, 250, 0.12);
  color: #c4b5fd;
}

.engine-badge .engine-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  box-shadow: 0 0 8px currentColor;
}

.engine-badge.is-loading .engine-dot {
  animation: engine-pulse 1.4s ease-in-out infinite;
}

@keyframes engine-pulse {
  0%,
  100% {
    opacity: 0.35;
  }
  50% {
    opacity: 1;
  }
}

.engine-model {
  font-size: 10.5px;
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.control-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px 14px;
  align-items: flex-end;
}

.ctrl-item {
  flex: 1 1 148px;
  min-width: 128px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.ctrl-item--switch {
  flex: 0 1 110px;
  min-width: 96px;
}

.ctrl-label {
  font-size: 11px;
  color: var(--qts-text-muted);
  line-height: 1;
}

.ctrl-action {
  flex: 0 0 auto;
  margin-left: auto;
}

.ctrl-action .el-button {
  height: 34px;
}

.option-hint {
  float: right;
  margin-left: 16px;
  font-size: 11px;
  color: var(--qts-text-dim);
}

.control-hint {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin: 14px 0 0;
  font-size: 11px;
  line-height: 1.7;
  color: var(--qts-text-dim);
}

.control-hint .el-icon {
  margin-top: 2px;
  flex: none;
}

.ai-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 408px;
  gap: 16px;
  align-items: start;
}

.ai-main {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.ai-side {
  position: sticky;
  top: 12px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.chart-head {
  gap: 12px;
  flex-wrap: wrap;
}

.chart-head-main {
  display: flex;
  align-items: baseline;
  gap: 10px;
  min-width: 0;
}

.chart-head-main .text-dim {
  font-size: 11px;
}

.chart-head-side {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-left: auto;
}

.chart-price {
  font-size: 16px;
  font-weight: 650;
}

.chart-change {
  font-size: 12px;
}

.chart-body {
  padding: 2px 8px 8px;
}

.chart-warning {
  padding: 8px 8px 0;
}

.chart-loading {
  padding: 18px 16px;
  min-height: 300px;
}

.ind-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.ind-tile {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding: 11px 13px;
  border-radius: var(--qts-radius-sm);
  border: 1px solid var(--qts-border);
  background: rgba(148, 163, 184, 0.04);
}

.ind-tile--wide {
  grid-column: span 2;
}

.ind-label {
  font-size: 10.5px;
  color: var(--qts-text-dim);
  letter-spacing: 0.02em;
}

.ind-value-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ind-value {
  font-size: 17px;
  font-weight: 650;
  line-height: 1.2;
}

.ind-hint {
  font-size: 10.5px;
  line-height: 1.5;
  color: var(--qts-text-muted);
}

.ind-bar {
  margin: 2px 0;
}

.sug-card {
  overflow: hidden;
}

.sug-hero {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 13px 14px;
  border-radius: 12px;
  border: 1px solid rgba(34, 211, 238, 0.18);
  background: linear-gradient(135deg, rgba(34, 211, 238, 0.09), rgba(167, 139, 250, 0.05));
}

.sug-title-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.sug-title-main {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.sug-name {
  font-size: 15px;
  font-weight: 650;
  line-height: 1.3;
}

.sug-sub {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.code-pill {
  padding: 1px 7px;
  border-radius: 5px;
  border: 1px solid var(--qts-border);
  background: rgba(15, 23, 42, 0.5);
  color: var(--qts-text-muted);
  font-family: var(--qts-mono);
  font-size: 10.5px;
}

.conf-badge {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  font-family: var(--qts-mono);
  font-size: 22px;
  font-weight: 700;
  line-height: 1.1;
  flex: none;
}

.conf-badge .conf-unit {
  font-size: 12px;
  margin-left: 1px;
}

.conf-badge small {
  font-family: var(--qts-font);
  font-size: 10px;
  font-weight: 400;
  color: var(--qts-text-dim);
  margin-top: 2px;
}

.conf-row {
  display: flex;
  align-items: center;
  gap: 9px;
}

.conf-label {
  font-size: 10.5px;
  color: var(--qts-text-dim);
  flex: none;
}

.conf-track {
  flex: 1;
  height: 6px;
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.14);
  overflow: hidden;
}

.conf-fill {
  height: 100%;
  border-radius: inherit;
  transition: width 0.35s ease;
}

.conf-value {
  font-size: 10.5px;
  color: var(--qts-text-muted);
  flex: none;
}

.sug-summary {
  margin: 12px 0 0;
  font-size: 12.5px;
  line-height: 1.75;
  color: var(--qts-text-muted);
}

.sug-section {
  margin-top: 14px;
}

.sug-section-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--qts-text);
  margin-bottom: 7px;
  letter-spacing: 0.02em;
}

.chip-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.param-chip {
  font-family: var(--qts-mono);
}

.risk-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.risk-tile {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 10px;
  border-radius: 9px;
  border: 1px solid var(--qts-border);
  background: rgba(148, 163, 184, 0.04);
}

.risk-tile span {
  font-size: 10px;
  color: var(--qts-text-dim);
}

.risk-tile b {
  font-size: 13px;
  font-weight: 650;
}

.risk-tile small {
  font-size: 9.5px;
  color: var(--qts-text-dim);
}

.reason-list,
.warn-list {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.reason-list li,
.warn-list li {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  font-size: 11.5px;
  line-height: 1.65;
  color: var(--qts-text-muted);
}

.reason-list li .el-icon {
  margin-top: 2px;
  color: var(--qts-success);
  flex: none;
}

.warn-list li .el-icon {
  margin-top: 2px;
  color: var(--qts-warning);
  flex: none;
}

.ai-risk {
  margin-top: 14px;
}

.sug-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}

.sug-actions .el-button {
  flex: 1 1 auto;
  margin-left: 0;
}

.sug-meta {
  margin: 12px 0 0;
  font-size: 10px;
  line-height: 1.7;
}

.analyzing-box {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  margin-bottom: 16px;
  border-radius: 12px;
  border: 1px solid rgba(34, 211, 238, 0.2);
  background: rgba(34, 211, 238, 0.06);
}

.analyzing-box .el-icon {
  font-size: 22px;
  color: var(--qts-accent);
}

.analyzing-box b {
  font-size: 13px;
}

.analyzing-box p {
  margin: 3px 0 0;
  font-size: 11px;
  color: var(--qts-text-dim);
}

.bt-loading {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 4px;
  font-size: 12px;
  color: var(--qts-text-muted);
}

.bt-loading .el-icon {
  color: var(--qts-accent);
  font-size: 18px;
}

.bt-error {
  margin-bottom: 12px;
}

.bt-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
}

.bt-tile {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 9px 10px;
  border-radius: 9px;
  border: 1px solid var(--qts-border);
  background: rgba(148, 163, 184, 0.04);
}

.bt-tile span {
  font-size: 10px;
  color: var(--qts-text-dim);
}

.bt-tile b {
  font-size: 14px;
  font-weight: 650;
}

.bt-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  margin-top: 10px;
  font-size: 10.5px;
  color: var(--qts-text-muted);
}

.bt-message {
  margin: 9px 0 2px;
  font-size: 10.5px;
  line-height: 1.6;
}

@media (max-width: 1360px) {
  .ai-layout {
    grid-template-columns: minmax(0, 1fr) 376px;
  }

  .ind-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .bt-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 1180px) {
  .ai-layout {
    grid-template-columns: 1fr;
  }

  .ai-side {
    position: static;
  }
}

@media (max-width: 768px) {
  .ctrl-action {
    width: 100%;
    margin-left: 0;
  }

  .ctrl-action .el-button {
    width: 100%;
  }

  .risk-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .chart-head-side {
    margin-left: 0;
    flex-wrap: wrap;
  }
}
</style>
