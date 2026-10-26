<template>
  <div class="page-stack">
    <PageHeader title="策略回测" subtitle="使用历史 K 线验证策略表现，回测与实盘共用同一套策略参数">
      <el-button :icon="Refresh" :loading="historyLoading" @click="loadHistory">刷新历史</el-button>
    </PageHeader>

    <div class="backtest-layout">
      <aside class="qts-card backtest-form-card">
        <div class="card-head">
          <div class="card-title">回测配置</div>
          <div class="card-extra"><span class="text-dim form-tip">回测可能耗时数秒</span></div>
        </div>
        <div class="card-body">
          <el-form
            ref="formRef"
            :model="form"
            :rules="rules"
            label-position="top"
            class="backtest-form"
            @submit.prevent="submitBacktest"
          >
            <el-form-item label="回测名称" prop="name">
              <el-input v-model="form.name" placeholder="例如：BTC 趋势策略 90 天" maxlength="60" />
            </el-form-item>

            <div class="form-grid-2">
              <el-form-item label="策略" prop="strategyCode">
                <el-select v-model="form.strategyCode" placeholder="选择策略" style="width: 100%" @change="onStrategyChange">
                  <el-option v-for="item in strategies" :key="item.code" :label="item.name" :value="item.code" />
                </el-select>
              </el-form-item>
              <el-form-item label="交易对" prop="instId">
                <el-select v-model="form.instId" placeholder="选择交易对" filterable style="width: 100%">
                  <el-option
                    v-for="item in instruments"
                    :key="item.instId"
                    :label="item.name || item.instId"
                    :value="item.instId"
                  />
                </el-select>
              </el-form-item>
            </div>

            <div class="form-grid-2">
              <el-form-item label="K 线周期" prop="bar">
                <el-select v-model="form.bar" style="width: 100%">
                  <el-option v-for="item in BAR_OPTIONS" :key="item.value" :label="item.label" :value="item.value" />
                </el-select>
              </el-form-item>
              <el-form-item label="初始资金 (USDT)" prop="initialCapital">
                <el-input-number
                  v-model="form.initialCapital"
                  :min="10"
                  :max="10000000"
                  :step="100"
                  controls-position="right"
                  style="width: 100%"
                />
              </el-form-item>
            </div>

            <el-form-item label="回测区间" prop="dateRange">
              <el-date-picker
                v-model="form.dateRange"
                type="daterange"
                value-format="YYYY-MM-DD"
                start-placeholder="开始日期"
                end-placeholder="结束日期"
                :clearable="false"
                style="width: 100%"
              />
            </el-form-item>

            <div class="form-section">
              <div class="form-section-title">策略参数</div>
              <div v-if="!strategySchema.length" class="text-dim form-section-empty">
                该策略没有可配置参数，将使用默认参数运行。
              </div>
              <div v-else class="form-grid-2">
                <el-form-item v-for="param in strategySchema" :key="param.code" :label="param.label || param.code">
                  <el-input-number
                    v-if="param.type === 'int' || param.type === 'decimal'"
                    v-model="form.params[param.code]"
                    :min="param.min ?? undefined"
                    :max="param.max ?? undefined"
                    :step="param.step ?? (param.type === 'int' ? 1 : 0.1)"
                    :precision="param.type === 'int' ? 0 : undefined"
                    controls-position="right"
                    style="width: 100%"
                  />
                  <el-switch v-else-if="param.type === 'bool'" v-model="form.params[param.code]" />
                  <el-select
                    v-else-if="param.type === 'select'"
                    v-model="form.params[param.code]"
                    style="width: 100%"
                  >
                    <el-option
                      v-for="option in param.options || []"
                      :key="option.value"
                      :label="option.label"
                      :value="option.value"
                    />
                  </el-select>
                  <el-input v-else v-model="form.params[param.code]" />
                </el-form-item>
              </div>
            </div>

            <div class="form-section">
              <div class="form-section-title">交易成本与风控</div>
              <div class="form-grid-2">
                <el-form-item label="手续费率 (%)">
                  <el-input-number v-model="form.feeRatePct" :min="0" :max="5" :step="0.01" :precision="4" controls-position="right" style="width: 100%" />
                </el-form-item>
                <el-form-item label="滑点 (%)">
                  <el-input-number v-model="form.slippagePct" :min="0" :max="5" :step="0.01" :precision="4" controls-position="right" style="width: 100%" />
                </el-form-item>
                <el-form-item label="杠杆 (倍)">
                  <el-input-number v-model="form.leverage" :min="1" :max="20" :step="1" :precision="0" controls-position="right" style="width: 100%" />
                  <span class="leverage-hint">仅回测模拟杠杆，不涉及真实借币</span>
                </el-form-item>
                <el-form-item label="仓位 (%)">
                  <el-input-number v-model="form.positionSizePct" :min="1" :max="100" :step="5" controls-position="right" style="width: 100%" />
                </el-form-item>
                <el-form-item label="止损 (%)">
                  <el-input-number v-model="form.stopLossPct" :min="0" :max="100" :step="0.5" controls-position="right" style="width: 100%" />
                </el-form-item>
                <el-form-item label="止盈 (%)">
                  <el-input-number v-model="form.takeProfitPct" :min="0" :max="1000" :step="0.5" controls-position="right" style="width: 100%" />
                </el-form-item>
                <el-form-item label="数据偏好">
                  <div class="switch-row">
                    <el-switch v-model="form.preferLocalData" />
                    <span class="switch-text">优先本地缓存</span>
                  </div>
                </el-form-item>
              </div>
            </div>

            <el-alert v-if="formError" class="form-alert" type="error" :title="formError" :closable="false" show-icon />

            <div class="form-actions">
              <el-button :disabled="submitting" @click="resetForm">重置</el-button>
              <el-button type="primary" native-type="submit" :icon="VideoPlay" :loading="submitting">
                {{ submitting ? '回测执行中…' : '开始回测' }}
              </el-button>
            </div>
          </el-form>
        </div>
      </aside>

      <section ref="resultTopRef" class="backtest-result">
        <div v-if="detailLoading" class="qts-card result-skeleton">
          <el-skeleton :rows="12" animated />
        </div>

        <div v-else-if="!result" class="qts-card result-empty">
          <EmptyState
            icon="DataAnalysis"
            title="尚未运行回测"
            description="在左侧选择策略、交易对与时间区间后点击「开始回测」，收益曲线、回撤与逐笔交易将展示在这里。"
          />
        </div>

        <template v-else>
          <section class="qts-card">
            <div class="card-head result-head">
              <div>
                <div class="result-title">{{ summary.name || '回测结果' }}</div>
                <div class="result-tags">
                  <el-tag size="small" effect="plain" type="info">{{ summary.strategyName || summary.strategyCode || '—' }}</el-tag>
                  <el-tag size="small" effect="plain" type="info">{{ summary.instId }} · {{ barLabel(summary.bar) }}</el-tag>
                  <el-tag size="small" effect="plain" type="info">{{ fmtDate(summary.startTime) }} ~ {{ fmtDate(summary.endTime) }}</el-tag>
                  <el-tag size="small" effect="plain" type="info">{{ Math.max(1, Number(summary.leverage || 1)) }}× 杠杆</el-tag>
                  <el-tag size="small" effect="plain" :type="summary.dataSource === 'synthetic' ? 'warning' : 'success'">
                    {{ summary.dataSource === 'synthetic' ? '示例数据' : 'OKX 数据' }}
                  </el-tag>
                </div>
              </div>
              <div class="result-head-actions">
                <el-button size="small" :icon="Delete" type="danger" plain :loading="deleting" @click="removeCurrent">删除</el-button>
              </div>
            </div>
            <div class="card-body">
              <el-alert
                v-if="summary.dataSource === 'synthetic'"
                class="result-alert"
                type="warning"
                :closable="false"
                show-icon
                title="本次回测使用示例行情数据"
                description="OKX 当前不可达，结果仅用于验证流程与参数，不代表真实市场表现。"
              />
              <el-alert
                v-if="summary.error"
                class="result-alert"
                type="error"
                :closable="false"
                show-icon
                :title="summary.error"
              />
              <div class="metric-grid">
                <div class="metric-tile">
                  <span class="metric-label">总收益率</span>
                  <b class="metric-value num" :class="pnlClass(summary.totalReturnPct)">{{ fmtPct(summary.totalReturnPct) }}</b>
                  <span class="metric-sub">基准 {{ fmtPct(benchmarkReturn) }}</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">最大回撤</span>
                  <b class="metric-value num is-loss">{{ maxDrawdownText }}</b>
                  <span class="metric-sub">峰值到谷底</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">夏普比率</span>
                  <b class="metric-value num">{{ fmtMoney(summary.sharpe, 2) }}</b>
                  <span class="metric-sub">风险调整后收益</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">胜率</span>
                  <b class="metric-value num">{{ fmtPct(summary.winRatePct, 2, false) }}</b>
                  <span class="metric-sub">共 {{ summary.tradeCount ?? 0 }} 笔交易</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">最终权益</span>
                  <b class="metric-value num">{{ fmtMoney(summary.finalEquity) }}</b>
                  <span class="metric-sub">初始 {{ fmtMoney(summary.initialCapital) }} USDT</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">手续费</span>
                  <b class="metric-value num">{{ fmtMoney(summary.fees ?? summary.fee) }}</b>
                  <span class="metric-sub">USDT</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">耗时</span>
                  <b class="metric-value num">{{ fmtDuration(summary.durationMs) }}</b>
                  <span class="metric-sub">{{ summary.status || '已完成' }}</span>
                </div>
                <div class="metric-tile">
                  <span class="metric-label">仓位 / 杠杆</span>
                  <b class="metric-value num">{{ fmtPct(summary.positionSizePct, 0, false) }}</b>
                  <span class="metric-sub">杠杆 {{ Math.max(1, Number(summary.leverage || 1)) }}× · 止损 {{ fmtPct(summary.stopLossPct, 1, false) }} · 止盈 {{ fmtPct(summary.takeProfitPct, 1, false) }}</span>
                </div>
              </div>
            </div>
          </section>

          <section class="qts-card">
            <div class="card-head">
              <div class="card-title">权益曲线与回撤</div>
              <div class="card-extra"><span class="text-dim">青色：权益 · 红色：回撤</span></div>
            </div>
            <div class="card-body card-body--chart">
              <EquityChart v-if="result.equityCurve?.length" :points="result.equityCurve" show-drawdown :height="320" />
              <EmptyState v-else compact icon="DataLine" title="暂无权益曲线数据" />
            </div>
          </section>

          <section class="qts-card">
            <div class="card-head">
              <div class="card-title">月度收益</div>
              <div class="card-extra"><span class="text-dim">按月统计收益率</span></div>
            </div>
            <div class="card-body card-body--chart">
              <MonthlyBarChart v-if="monthlyReturns.length" :data="monthlyReturns" :height="220" />
              <EmptyState v-else compact icon="Histogram" title="暂无月度收益数据" />
            </div>
          </section>

          <section class="qts-card">
            <div class="card-head">
              <div class="card-title">交易明细</div>
              <div class="card-extra"><span class="text-dim">共 {{ trades.length }} 笔</span></div>
            </div>
            <div class="card-body card-body--flush">
              <el-table :data="pagedTrades" size="small" style="width: 100%">
                <el-table-column label="#" width="52" align="center">
                  <template #default="{ $index }">
                    <span class="text-dim num">{{ (tradePage - 1) * tradePageSize + $index + 1 }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="方向" width="76" align="center">
                  <template #default="{ row }">
                    <el-tag v-if="row.side" size="small" effect="plain" :type="sideMeta(row.side).type">
                      {{ sideMeta(row.side).label }}
                    </el-tag>
                    <span v-else class="text-dim">—</span>
                  </template>
                </el-table-column>
                <el-table-column label="开仓时间" width="148">
                  <template #default="{ row }"><span class="num text-muted">{{ fmtDateTime(row.entryTime) }}</span></template>
                </el-table-column>
                <el-table-column label="平仓时间" width="148">
                  <template #default="{ row }"><span class="num text-muted">{{ fmtDateTime(row.exitTime) }}</span></template>
                </el-table-column>
                <el-table-column label="开仓价" width="104" align="right">
                  <template #default="{ row }"><span class="num">{{ fmtPrice(row.entryPrice) }}</span></template>
                </el-table-column>
                <el-table-column label="平仓价" width="104" align="right">
                  <template #default="{ row }"><span class="num">{{ fmtPrice(row.exitPrice) }}</span></template>
                </el-table-column>
                <el-table-column label="数量" width="100" align="right">
                  <template #default="{ row }"><span class="num">{{ fmtNumber(row.quantity, 4) }}</span></template>
                </el-table-column>
                <el-table-column label="盈亏" width="106" align="right">
                  <template #default="{ row }">
                    <b class="num" :class="pnlClass(row.pnl)">{{ fmtMoney(row.pnl) }}</b>
                  </template>
                </el-table-column>
                <el-table-column label="收益率" width="92" align="right">
                  <template #default="{ row }">
                    <span class="num" :class="pnlClass(row.pnlPct)">{{ fmtPct(row.pnlPct) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="退出原因" width="96">
                  <template #default="{ row }"><el-tag size="small" effect="plain" type="info">{{ exitReasonLabel(row.exitReason) }}</el-tag></template>
                </el-table-column>
                <el-table-column label="持仓K线" width="86" align="right">
                  <template #default="{ row }"><span class="num text-muted">{{ row.holdingBars ?? '—' }}</span></template>
                </el-table-column>
                <el-table-column label="手续费" min-width="88" align="right">
                  <template #default="{ row }"><span class="num text-muted">{{ fmtMoney(row.fee) }}</span></template>
                </el-table-column>
                <template #empty>
                  <EmptyState compact icon="Tickets" title="本次回测没有产生交易" description="可能是策略信号未触发，或回测区间内数据不足。" />
                </template>
              </el-table>
            </div>
            <div v-if="trades.length > tradePageSize" class="card-foot trades-foot">
              <el-pagination
                v-model:current-page="tradePage"
                :page-size="tradePageSize"
                :total="trades.length"
                layout="total, prev, pager, next"
                background
                small
              />
            </div>
          </section>
        </template>
      </section>
    </div>

    <section class="qts-card">
      <div class="card-head">
        <div class="card-title">历史回测</div>
        <div class="card-extra history-filters">
          <el-select v-model="historyFilterStrategy" size="small" clearable placeholder="全部策略" style="width: 150px" @change="onHistoryFilter">
            <el-option v-for="item in strategies" :key="item.code" :label="item.name" :value="item.code" />
          </el-select>
          <el-select v-model="historyFilterInst" size="small" clearable placeholder="全部交易对" style="width: 140px" @change="onHistoryFilter">
            <el-option v-for="item in instruments" :key="item.instId" :label="item.instId" :value="item.instId" />
          </el-select>
          <el-button size="small" :icon="Refresh" :loading="historyLoading" @click="loadHistory">刷新</el-button>
        </div>
      </div>
      <div class="card-body card-body--flush">
        <el-table v-loading="historyLoading" :data="historyItems" size="small" style="width: 100%">
          <el-table-column label="名称" min-width="180" show-overflow-tooltip>
            <template #default="{ row }">
              <div class="history-name">
                <span>{{ row.name }}</span>
                <el-tag v-if="row.dataSource === 'synthetic'" size="small" effect="plain" type="warning">示例</el-tag>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="策略" width="150" show-overflow-tooltip>
            <template #default="{ row }"><span class="text-muted">{{ row.strategyName || row.strategyCode }}</span></template>
          </el-table-column>
          <el-table-column label="交易对/周期" width="130">
            <template #default="{ row }">
              <span class="mono">{{ row.instId }}</span>
              <span class="text-dim"> · {{ barLabel(row.bar) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="区间收益" width="106" align="right">
            <template #default="{ row }">
              <b class="num" :class="pnlClass(row.totalReturnPct)">{{ fmtPct(row.totalReturnPct) }}</b>
            </template>
          </el-table-column>
          <el-table-column label="最大回撤" width="100" align="right">
            <template #default="{ row }"><span class="num is-loss">{{ fmtPct(row.maxDrawdownPct ? -Math.abs(row.maxDrawdownPct) : row.maxDrawdownPct) }}</span></template>
          </el-table-column>
          <el-table-column label="夏普" width="84" align="right">
            <template #default="{ row }"><span class="num">{{ fmtMoney(row.sharpe, 2) }}</span></template>
          </el-table-column>
          <el-table-column label="胜率" width="88" align="right">
            <template #default="{ row }"><span class="num">{{ fmtPct(row.winRatePct, 1, false) }}</span></template>
          </el-table-column>
          <el-table-column label="交易" width="70" align="right">
            <template #default="{ row }"><span class="num text-muted">{{ row.tradeCount ?? '—' }}</span></template>
          </el-table-column>
          <el-table-column label="创建时间" width="150">
            <template #default="{ row }"><span class="num text-muted">{{ fmtDateTime(row.createdAt) }}</span></template>
          </el-table-column>
          <el-table-column label="操作" width="118" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" size="small" @click="viewHistory(row.id)">查看</el-button>
              <el-button link type="danger" size="small" @click="removeHistory(row)">删除</el-button>
            </template>
          </el-table-column>
          <template #empty>
            <EmptyState compact icon="DataAnalysis" title="暂无回测记录" description="运行第一次回测后，历史记录会展示在这里。" />
          </template>
        </el-table>
      </div>
      <div v-if="historyTotal > historyPageSize" class="card-foot">
        <el-pagination
          v-model:current-page="historyPage"
          :page-size="historyPageSize"
          :total="historyTotal"
          layout="total, prev, pager, next"
          background
          small
          @current-change="loadHistory"
        />
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Refresh, VideoPlay } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import EmptyState from '@/components/EmptyState.vue'
import EquityChart from '@/components/EquityChart.vue'
import MonthlyBarChart from '@/components/MonthlyBarChart.vue'
import { backtestApi, marketApi, strategyApi, normalizePaged } from '@/api'
import { BAR_OPTIONS, DEFAULT_INSTRUMENTS, barLabel, sideMeta } from '@/utils/constants'
import {
  fmtDate,
  fmtDateTime,
  fmtDuration,
  fmtMoney,
  fmtNumber,
  fmtPct,
  fmtPrice,
  pnlClass
} from '@/utils/format'

const route = useRoute()

const EXIT_REASON_LABELS = {
  StopLoss: '止损',
  TakeProfit: '止盈',
  Signal: '信号平仓',
  EndOfData: '回测结束',
  ForceClose: '强制平仓',
  TrailingStop: '移动止损'
}

const formRef = ref(null)
const resultTopRef = ref(null)
const strategies = ref([])
const instruments = ref(DEFAULT_INSTRUMENTS.map((item) => ({ ...item })))
const submitting = ref(false)
const formError = ref('')
const result = ref(null)
const detailLoading = ref(false)
const deleting = ref(false)

const historyItems = ref([])
const historyTotal = ref(0)
const historyPage = ref(1)
const historyPageSize = ref(8)
const historyLoading = ref(false)
const historyFilterStrategy = ref('')
const historyFilterInst = ref('')

const tradePage = ref(1)
const tradePageSize = ref(10)

function formatDateValue(date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function defaultDateRange() {
  const end = new Date()
  const start = new Date()
  start.setDate(start.getDate() - 90)
  return [formatDateValue(start), formatDateValue(end)]
}

const form = reactive({
  name: '',
  strategyCode: '',
  instId: 'BTC-USDT',
  bar: '1H',
  initialCapital: 100,
  dateRange: defaultDateRange(),
  params: {},
  feeRatePct: 0.05,
  slippagePct: 0.02,
  leverage: 1,
  positionSizePct: 100,
  stopLossPct: 0,
  takeProfitPct: 0,
  preferLocalData: false
})

const rules = {
  name: [{ required: true, message: '请输入回测名称', trigger: 'blur' }],
  strategyCode: [{ required: true, message: '请选择策略', trigger: 'change' }],
  instId: [{ required: true, message: '请选择交易对', trigger: 'change' }],
  bar: [{ required: true, message: '请选择 K 线周期', trigger: 'change' }],
  initialCapital: [
    {
      validator(_rule, value, callback) {
        const num = Number(value)
        if (!Number.isFinite(num) || num < 10) callback(new Error('初始资金不能小于 10 USDT'))
        else callback()
      },
      trigger: 'change'
    }
  ],
  dateRange: [
    {
      validator(_rule, value, callback) {
        if (!Array.isArray(value) || value.length !== 2) {
          callback(new Error('请选择回测时间区间'))
          return
        }
        if (new Date(value[0]).getTime() > new Date(value[1]).getTime()) {
          callback(new Error('开始日期必须早于结束日期'))
          return
        }
        callback()
      },
      trigger: 'change'
    }
  ]
}

const selectedStrategy = computed(
  () => strategies.value.find((item) => item.code === form.strategyCode) || null
)

const strategySchema = computed(() => selectedStrategy.value?.paramsSchema || [])
const summary = computed(() => result.value?.summary || {})
const trades = computed(() => result.value?.trades || [])
const monthlyReturns = computed(() => result.value?.monthlyReturns || [])
const benchmarkReturn = computed(() => result.value?.benchmark?.buyAndHoldReturnPct ?? null)
const pagedTrades = computed(() => {
  const start = (tradePage.value - 1) * tradePageSize.value
  return trades.value.slice(start, start + tradePageSize.value)
})
const maxDrawdownText = computed(() => {
  const value = summary.value.maxDrawdownPct
  if (value === null || value === undefined || value === '') return '—'
  return fmtPct(-Math.abs(Number(value)))
})

function exitReasonLabel(reason) {
  return EXIT_REASON_LABELS[reason] || reason || '—'
}

function applyStrategyDefaults(strategy) {
  const params = {}
  for (const param of strategy?.paramsSchema || []) {
    const fromDefaults = strategy?.defaultParams?.[param.code]
    params[param.code] = fromDefaults !== undefined ? fromDefaults : param.default
  }
  form.params = params
}

function onStrategyChange(code) {
  const strategy = strategies.value.find((item) => item.code === code)
  applyStrategyDefaults(strategy)
  if (!form.name) form.name = `${strategy?.name || '策略'} 回测`
}

async function loadStrategies() {
  try {
    const res = await strategyApi.list()
    strategies.value = Array.isArray(res) ? res : res?.items || []
    if (strategies.value.length) {
      const fromQuery = typeof route.query.strategy === 'string' ? route.query.strategy : ''
      const matched = strategies.value.find((item) => item.code === fromQuery) || strategies.value[0]
      form.strategyCode = matched.code
      form.name = form.name || `${matched.name} 回测`
      applyStrategyDefaults(matched)
    }
  } catch (err) {
    ElMessage.error(err?.message || '策略列表加载失败')
  }
}

async function loadInstruments() {
  try {
    const res = await marketApi.instruments()
    if (Array.isArray(res) && res.length) instruments.value = res
  } catch {
    // 行情接口不可用时使用默认交易对
  }
}

async function loadHistory() {
  historyLoading.value = true
  try {
    const res = await backtestApi.list({
      page: historyPage.value,
      pageSize: historyPageSize.value,
      strategyCode: historyFilterStrategy.value,
      instId: historyFilterInst.value
    })
    const paged = normalizePaged(res, historyPage.value, historyPageSize.value)
    historyItems.value = paged.items
    historyTotal.value = paged.total
  } catch (err) {
    ElMessage.error(err?.message || '历史回测加载失败')
  } finally {
    historyLoading.value = false
  }
}

function onHistoryFilter() {
  historyPage.value = 1
  loadHistory()
}

function buildPayload() {
  const [start, end] = form.dateRange
  return {
    name: form.name.trim(),
    strategyCode: form.strategyCode,
    instId: form.instId,
    bar: form.bar,
    startTime: new Date(`${start}T00:00:00`).toISOString(),
    endTime: new Date(`${end}T23:59:59`).toISOString(),
    initialCapital: Number(form.initialCapital),
    params: { ...form.params },
    feeRatePct: Number(form.feeRatePct),
    slippagePct: Number(form.slippagePct),
    leverage: Number(form.leverage),
    positionSizePct: Number(form.positionSizePct),
    stopLossPct: Number(form.stopLossPct),
    takeProfitPct: Number(form.takeProfitPct),
    preferLocalData: Boolean(form.preferLocalData)
  }
}

async function loadDetail(id, scroll = false) {
  detailLoading.value = true
  result.value = null
  tradePage.value = 1
  try {
    const res = await backtestApi.detail(id)
    result.value = res || null
    if (scroll) {
      await nextTick()
      resultTopRef.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  } catch (err) {
    ElMessage.error(err?.message || '回测详情加载失败')
  } finally {
    detailLoading.value = false
  }
}

async function submitBacktest() {
  if (!formRef.value) return
  try {
    await formRef.value.validate()
  } catch {
    return
  }
  submitting.value = true
  formError.value = ''
  try {
    const res = await backtestApi.create(buildPayload())
    if (res?.id) {
      ElMessage.success(res?.message || '回测完成')
      await Promise.all([loadDetail(res.id, true), loadHistory()])
    } else {
      ElMessage.warning(res?.message || '回测已提交，但未返回结果')
    }
  } catch (err) {
    formError.value = err?.message || '回测执行失败'
    ElMessage.error(formError.value)
  } finally {
    submitting.value = false
  }
}

async function removeById(id) {
  await backtestApi.remove(id)
}

async function removeCurrent() {
  if (!summary.value.id) return
  try {
    await ElMessageBox.confirm('删除后不可恢复，确认删除该回测记录？', '删除回测', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }
  deleting.value = true
  try {
    await removeById(summary.value.id)
    ElMessage.success('回测已删除')
    result.value = null
    await loadHistory()
  } catch (err) {
    ElMessage.error(err?.message || '删除失败')
  } finally {
    deleting.value = false
  }
}

async function removeHistory(row) {
  try {
    await ElMessageBox.confirm(`确认删除回测「${row.name}」？`, '删除回测', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }
  try {
    await removeById(row.id)
    ElMessage.success('回测已删除')
    if (summary.value.id === row.id) result.value = null
    if (historyItems.value.length === 1 && historyPage.value > 1) historyPage.value -= 1
    await loadHistory()
  } catch (err) {
    ElMessage.error(err?.message || '删除失败')
  }
}

function viewHistory(id) {
  loadDetail(id, true)
}

function resetForm() {
  form.name = ''
  form.dateRange = defaultDateRange()
  form.initialCapital = 100
  form.feeRatePct = 0.05
  form.slippagePct = 0.02
  form.leverage = 1
  form.positionSizePct = 100
  form.stopLossPct = 0
  form.takeProfitPct = 0
  form.preferLocalData = false
  form.params = {}
  formError.value = ''
  const strategy = selectedStrategy.value || strategies.value[0]
  if (strategy) {
    form.strategyCode = strategy.code
    applyStrategyDefaults(strategy)
  }
  formRef.value?.clearValidate()
}

onMounted(async () => {
  await Promise.all([loadStrategies(), loadInstruments()])
  loadHistory()
})
</script>

<style scoped>
.backtest-layout {
  display: grid;
  grid-template-columns: 384px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.backtest-form-card {
  position: sticky;
  top: 80px;
  max-height: calc(100vh - 104px);
  display: flex;
  flex-direction: column;
}

.backtest-form-card > .card-body {
  overflow-y: auto;
  overscroll-behavior: contain;
}

.form-tip {
  font-size: 10.5px;
}

.backtest-form :deep(.el-form-item) {
  margin-bottom: 14px;
}

.backtest-form :deep(.el-form-item__label) {
  padding-bottom: 4px;
  font-size: 11.5px;
  color: var(--qts-text-muted);
}

.form-section {
  padding-top: 6px;
  margin-top: 4px;
  border-top: 1px solid rgba(148, 163, 184, 0.08);
}

.form-section-title {
  font-size: 12px;
  font-weight: 600;
  color: #cbd5e1;
  letter-spacing: 0.6px;
  margin: 12px 0 10px;
}

.form-section-empty {
  font-size: 11.5px;
  padding-bottom: 8px;
}

.switch-row {
  display: flex;
  align-items: center;
  gap: 9px;
  height: 32px;
}

.switch-text {
  font-size: 11.5px;
  color: var(--qts-text-dim);
}

.leverage-hint {
  display: block;
  margin-top: 4px;
  font-size: 11px;
  line-height: 1.4;
  color: var(--qts-text-dim);
}

.form-alert {
  margin: 6px 0 14px;
}

.form-actions {
  display: flex;
  gap: 10px;
  padding-top: 14px;
  border-top: 1px solid rgba(148, 163, 184, 0.08);
  position: sticky;
  bottom: -18px;
  background: linear-gradient(180deg, rgba(13, 20, 36, 0.6), rgba(13, 20, 36, 0.98) 40%);
  padding-bottom: 4px;
}

.form-actions .el-button {
  flex: 1;
}

/* 结果区 */
.backtest-result {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  scroll-margin-top: 80px;
}

.result-skeleton {
  padding: 24px;
}

.result-empty {
  min-height: 420px;
  display: grid;
  place-items: center;
}

.result-head {
  align-items: flex-start;
}

.result-title {
  font-size: 17px;
  font-weight: 700;
  color: #f1f5f9;
  letter-spacing: 0.3px;
}

.result-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 9px;
}

.result-head-actions {
  flex-shrink: 0;
}

.result-alert {
  margin-bottom: 14px;
}

.metric-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.metric-tile {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding: 12px 14px;
  border-radius: 11px;
  border: 1px solid rgba(148, 163, 184, 0.1);
  background: rgba(148, 163, 184, 0.03);
}

.metric-label {
  font-size: 11px;
  color: var(--qts-text-dim);
  letter-spacing: 0.3px;
}

.metric-value {
  font-size: 19px;
  font-weight: 700;
  color: var(--qts-text);
  line-height: 1.25;
}

.metric-sub {
  font-size: 10.5px;
  color: var(--qts-text-dim);
}

.trades-foot {
  display: flex;
  justify-content: flex-end;
}

/* 历史表 */
.history-filters {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.history-name {
  display: flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
}

.history-name > span:first-child {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono {
  font-family: var(--qts-mono);
  font-size: 12px;
  color: #cbd5e1;
}

@media (max-width: 1440px) {
  .metric-grid {
    grid-template-columns: repeat(4, minmax(0, 1fr));
  }
}

@media (max-width: 1280px) {
  .backtest-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .backtest-form-card {
    position: static;
    max-height: none;
  }
}

@media (max-width: 900px) {
  .metric-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 560px) {
  .metric-grid {
    grid-template-columns: repeat(1, minmax(0, 1fr));
  }
}
</style>
