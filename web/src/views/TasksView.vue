<template>
  <div class="page-stack">
    <PageHeader title="任务中心" subtitle="策略任务在已配置的欧易 API 账户中持续执行信号">
      <el-button :icon="Refresh" :loading="loading" @click="loadTasks">刷新</el-button>
      <el-button type="primary" :icon="Plus" @click="openCreate">新建任务</el-button>
    </PageHeader>

    <div class="task-chips">
      <span class="task-chip">
        任务总数
        <b class="num">{{ stats.totalTasks ?? 0 }}</b>
      </span>
      <span class="task-chip task-chip--success">
        运行中
        <b class="num">{{ stats.runningTasks ?? 0 }}</b>
      </span>
      <span class="task-chip">
        持仓数
        <b class="num">{{ stats.openPositions ?? 0 }}</b>
      </span>
      <span class="task-chip" :class="Number(stats.todayPnl ?? 0) >= 0 ? 'task-chip--success' : 'task-chip--danger'">
        今日盈亏
        <b class="num">{{ fmtMoney(stats.todayPnl) }}</b>
      </span>
      <span class="task-chip task-chip--muted">列表每 10 秒自动刷新</span>
    </div>

    <el-alert v-if="error" type="error" :title="error" show-icon :closable="false" />

    <section class="qts-card">
      <div class="card-head">
        <div class="card-title">策略任务</div>
        <div class="card-extra task-filters">
          <el-select v-model="statusFilter" size="small" style="width: 132px" @change="onFilterChange">
            <el-option v-for="item in TASK_STATUS_OPTIONS" :key="item.value" :label="item.label" :value="item.value" />
          </el-select>
          <el-button size="small" :icon="Refresh" :loading="loading" @click="loadTasks">刷新</el-button>
        </div>
      </div>
      <div class="card-body card-body--flush">
        <el-table v-loading="loading" :data="items" size="small" style="width: 100%">
          <el-table-column label="任务" min-width="200">
            <template #default="{ row }">
              <div class="task-name-cell">
                <span class="task-name">{{ row.name }}</span>
                <span class="task-strategy">{{ row.strategyName || row.strategyCode }} · {{ row.instId }} {{ barLabel(row.bar) }}</span>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="模式" width="86">
            <template #default="{ row }">
              <el-tag size="small" effect="plain" :type="row.mode === 'Live' ? 'danger' : 'info'">
                {{ modeLabel(row.mode) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="106">
            <template #default="{ row }">
              <span class="task-status" :class="`is-${String(row.status || '').toLowerCase()}`">
                <i class="status-dot"></i>
                {{ taskStatusMeta(row.status).label }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="分配资金" width="112" align="right">
            <template #default="{ row }">
              <span class="num">{{ fmtMoney(row.allocatedCapital) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="杠杆 / 滑点" width="88" align="right">
            <template #default="{ row }">
              <div class="num lev-cell">
                <b>{{ Math.max(1, Number(row.leverage || 1)) }}×</b>
                <span>{{ fmtPct(row.slippagePct ?? 0.05, 2, false) }}</span>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="总盈亏" width="112" align="right">
            <template #default="{ row }">
              <b class="num" :class="pnlClass(row.totalPnl)">{{ fmtMoney(row.totalPnl) }}</b>
            </template>
          </el-table-column>
          <el-table-column label="最近信号" width="106">
            <template #default="{ row }">
              <el-tag v-if="row.lastSignal" size="small" effect="plain" :type="signalMeta(row.lastSignal).type">
                {{ signalMeta(row.lastSignal).label }}
              </el-tag>
              <span v-else class="text-dim">—</span>
            </template>
          </el-table-column>
          <el-table-column label="持仓" width="106" align="right">
            <template #default="{ row }">
              <span v-if="Number(row.positionQty) > 0" class="num">
                {{ fmtNumber(row.positionQty, 4) }}
                <span class="text-dim">@ {{ fmtPrice(row.positionAvgPrice) }}</span>
              </span>
              <span v-else class="text-dim">空仓</span>
            </template>
          </el-table-column>
          <el-table-column label="最近运行" width="150">
            <template #default="{ row }">
              <span class="num text-muted">{{ fmtDateTime(row.lastRunAt) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="286" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" size="small" @click="openDetail(row)">详情</el-button>
              <el-button
                v-if="Number(row.positionQty) > 0"
                link
                type="warning"
                size="small"
                :loading="actionLoading === row.id"
                @click="closePosition(row)"
              >
                平仓
              </el-button>
              <el-button
                v-if="row.status === 'Running'"
                link
                type="warning"
                size="small"
                :loading="actionLoading === row.id"
                @click="stopTask(row)"
              >
                停止
              </el-button>
              <template v-else>
                <el-button
                  link
                  type="success"
                  size="small"
                  :loading="actionLoading === row.id"
                  @click="startTask(row)"
                >
                  启动
                </el-button>
                <el-button
                  v-if="row.status === 'Stopped' || row.status === 'Error'"
                  link
                  type="primary"
                  size="small"
                  @click="openEdit(row)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="row.status === 'Stopped'"
                  link
                  type="danger"
                  size="small"
                  @click="deleteTask(row)"
                >
                  删除
                </el-button>
              </template>
            </template>
          </el-table-column>
          <template #empty>
            <EmptyState
              compact
              icon="AlarmClock"
              title="暂无策略任务"
              description="创建任务后，系统会按 K 线周期自动执行策略信号。"
            >
              <el-button size="small" type="primary" :icon="Plus" @click="openCreate">新建任务</el-button>
            </EmptyState>
          </template>
        </el-table>
      </div>
      <div v-if="total > pageSize" class="card-foot">
        <el-pagination
          v-model:current-page="page"
          :page-size="pageSize"
          :total="total"
          layout="total, prev, pager, next"
          background
          small
          @current-change="loadTasks"
        />
      </div>
    </section>

    <el-dialog
      v-model="dialogVisible"
      :title="dialogMode === 'create' ? '新建策略任务' : '编辑策略任务'"
      width="680px"
      :close-on-click-modal="false"
      destroy-on-close
      class="task-dialog"
    >
      <el-form ref="dialogFormRef" :model="form" :rules="formRules" label-position="top" class="task-form">
        <el-form-item label="任务名称" prop="name">
          <el-input v-model="form.name" placeholder="例如：BTC 趋势策略 · API 账户" maxlength="60" />
        </el-form-item>

        <div class="form-grid-2">
          <el-form-item label="策略" prop="strategyCode">
            <el-select v-model="form.strategyCode" style="width: 100%" :disabled="dialogMode === 'edit'" @change="onStrategyChange">
              <el-option v-for="item in strategies" :key="item.code" :label="item.name" :value="item.code" />
            </el-select>
          </el-form-item>
          <el-form-item label="交易对" prop="instId">
            <el-select v-model="form.instId" filterable style="width: 100%" :disabled="dialogMode === 'edit'">
              <el-option v-for="item in instruments" :key="item.instId" :label="item.name || item.instId" :value="item.instId" />
            </el-select>
          </el-form-item>
        </div>

        <div class="form-grid-2">
          <el-form-item label="K 线周期" prop="bar">
            <el-select v-model="form.bar" style="width: 100%" :disabled="dialogMode === 'edit'">
              <el-option v-for="item in BAR_OPTIONS" :key="item.value" :label="item.label" :value="item.value" />
            </el-select>
          </el-form-item>
          <el-form-item label="分配资金 (USDT)" prop="allocatedCapital">
            <el-input-number
              v-model="form.allocatedCapital"
              :min="10"
              :max="10000000"
              :step="100"
              controls-position="right"
              style="width: 100%"
            />
          </el-form-item>
        </div>

        <el-form-item label="运行模式">
          <div class="switch-row">
            <el-tag type="danger" effect="dark">实盘 · OKX API 账户</el-tag>
            <span class="switch-text">策略信号直接发送至已配置的欧易账户，不再使用纸面账户</span>
          </div>
        </el-form-item>

        <el-form-item v-if="dialogMode === 'create'" label="创建后立即启动">
          <div class="switch-row">
            <el-switch v-model="form.startImmediately" />
            <span class="switch-text">启动后任务会按 K 线周期自动执行信号</span>
          </div>
        </el-form-item>

        <div class="form-section">
          <div class="form-section-title">策略参数</div>
          <div v-if="!strategySchema.length" class="text-dim form-section-empty">该策略没有可配置参数，将使用默认参数。</div>
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
              <el-select v-else-if="param.type === 'select'" v-model="form.params[param.code]" style="width: 100%">
                <el-option v-for="option in param.options || []" :key="option.value" :label="option.label" :value="option.value" />
              </el-select>
              <el-input v-else v-model="form.params[param.code]" />
            </el-form-item>
          </div>
        </div>

        <div class="form-section">
          <div class="form-section-title">风险控制</div>
          <div class="form-grid-2">
            <el-form-item label="杠杆 (倍)" prop="leverage">
              <el-input-number
                v-model="form.leverage"
                :min="1"
                :max="20"
                :step="1"
                :precision="0"
                controls-position="right"
                style="width: 100%"
              />
            </el-form-item>
            <el-form-item label="滑点 (%)" prop="slippagePct">
              <el-input-number
                v-model="form.slippagePct"
                :min="0"
                :max="5"
                :step="0.01"
                :precision="2"
                controls-position="right"
                style="width: 100%"
              />
            </el-form-item>
            <el-form-item label="止损 (%)" prop="risk.stopLossPct">
              <el-input-number v-model="form.risk.stopLossPct" :min="0" :max="100" :step="0.5" controls-position="right" style="width: 100%" />
            </el-form-item>
            <el-form-item label="止盈 (%)" prop="risk.takeProfitPct">
              <el-input-number v-model="form.risk.takeProfitPct" :min="0" :max="1000" :step="0.5" controls-position="right" style="width: 100%" />
            </el-form-item>
            <el-form-item label="最大仓位 (%)" prop="risk.maxPositionPct">
              <el-input-number v-model="form.risk.maxPositionPct" :min="1" :max="100" :step="5" controls-position="right" style="width: 100%" />
            </el-form-item>
          </div>
          <p class="risk-tip">
            杠杆 1–20 倍，滑点用于模拟成交价格偏差；止损/止盈填 0 表示不启用对应风控；最大仓位限制单次开仓占分配资金的比例。
          </p>
        </div>

        <el-alert
          v-if="form.mode === 'Live'"
          type="error"
          :closable="false"
          show-icon
          title="实盘模式将使用欧易账户中的真实资金下单"
          description="1× 多单走现货；杠杆 >1 或做空会自动走 USDT 永续合约（逐仓）。请确认已在设置页配置 API Key 并开启实盘交易，策略信号会直接下发真实委托，请自行评估风险。"
        />
        <el-checkbox v-if="form.mode === 'Live' && dialogMode === 'create'" v-model="form.liveRiskConfirmed" class="live-confirm">
          我已了解实盘合约与杠杆风险，确认使用该 API 账户运行任务
        </el-checkbox>

        <el-alert v-if="formError" class="form-alert" type="error" :title="formError" :closable="false" show-icon />
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitForm">
          {{ dialogMode === 'create' ? '创建任务' : '保存修改' }}
        </el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="drawerVisible" title="任务详情" size="500px" class="task-drawer">
      <div v-if="detailLoading" class="drawer-loading">
        <el-skeleton :rows="8" animated />
      </div>
      <template v-else-if="detail">
        <div class="drawer-head">
          <div>
            <div class="drawer-title">{{ detail.name }}</div>
            <div class="drawer-tags">
              <el-tag size="small" effect="plain" :type="detail.mode === 'Live' ? 'danger' : 'info'">
                {{ modeLabel(detail.mode) }}
              </el-tag>
              <el-tag size="small" effect="plain" :type="taskStatusMeta(detail.status).type">
                {{ taskStatusMeta(detail.status).label }}
              </el-tag>
              <el-tag size="small" effect="plain" type="info">
                {{ detail.strategyName || detail.strategyCode }} · {{ detail.instId }} {{ barLabel(detail.bar) }}
              </el-tag>
            </div>
          </div>
          <div class="drawer-actions">
            <el-button
              v-if="detail.status === 'Running'"
              size="small"
              type="warning"
              plain
              :loading="actionLoading === detail.id"
              @click="stopTask(detail)"
            >
              停止任务
            </el-button>
            <el-button
              v-else
              size="small"
              type="success"
              plain
              :loading="actionLoading === detail.id"
              @click="startTask(detail)"
            >
              启动任务
            </el-button>
          </div>
        </div>

        <div class="drawer-metrics">
          <div class="drawer-metric">
            <span>总盈亏</span>
            <b class="num" :class="pnlClass(detail.totalPnl)">{{ fmtMoney(detail.totalPnl) }} USDT</b>
          </div>
          <div class="drawer-metric">
            <span>分配资金</span>
            <b class="num">{{ fmtMoney(detail.allocatedCapital) }} USDT</b>
          </div>
          <div class="drawer-metric">
            <span>持仓数量</span>
            <b class="num">{{ fmtNumber(detail.positionQty, 4) }}</b>
          </div>
          <div class="drawer-metric">
            <span>持仓均价</span>
            <b class="num">{{ fmtPrice(detail.positionAvgPrice) }}</b>
          </div>
        </div>

        <el-descriptions :column="2" border size="small" class="drawer-desc">
          <el-descriptions-item label="最近信号">{{ signalMeta(detail.lastSignal).label }}</el-descriptions-item>
          <el-descriptions-item label="最近运行">{{ fmtDateTime(detail.lastRunAt) }}</el-descriptions-item>
          <el-descriptions-item label="创建时间">{{ fmtDateTime(detail.createdAt) }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ fmtDateTime(detail.updatedAt) }}</el-descriptions-item>
          <el-descriptions-item label="止损">{{ fmtPct(detail.risk?.stopLossPct, 2, false) }}</el-descriptions-item>
          <el-descriptions-item label="止盈">{{ fmtPct(detail.risk?.takeProfitPct, 2, false) }}</el-descriptions-item>
          <el-descriptions-item label="最大仓位">{{ fmtPct(detail.risk?.maxPositionPct, 0, false) }}</el-descriptions-item>
          <el-descriptions-item label="杠杆">{{ Math.max(1, Number(detail.leverage || 1)) }}×</el-descriptions-item>
          <el-descriptions-item label="滑点">{{ fmtPct(detail.slippagePct ?? 0.05, 2, false) }}</el-descriptions-item>
          <el-descriptions-item label="任务 ID">#{{ detail.id }}</el-descriptions-item>
        </el-descriptions>

        <el-alert
          v-if="detail.lastError"
          class="drawer-alert"
          type="error"
          :title="detail.lastError"
          :closable="false"
          show-icon
        />

        <div class="drawer-section">
          <div class="drawer-section-title">策略参数</div>
          <div v-if="paramEntries.length" class="param-list">
            <span v-for="entry in paramEntries" :key="entry.key" class="param-chip">
              <em>{{ entry.key }}</em>
              <b>{{ entry.value }}</b>
            </span>
          </div>
          <div v-else class="text-dim">使用策略默认参数</div>
        </div>

        <div class="drawer-section">
          <div class="drawer-section-title">最近订单</div>
          <el-table :data="taskOrders" size="small" style="width: 100%">
            <el-table-column label="时间" width="140">
              <template #default="{ row }"><span class="num text-muted">{{ fmtDateTime(row.createdAt) }}</span></template>
            </el-table-column>
            <el-table-column label="方向" width="66">
              <template #default="{ row }">
                <span :class="row.side === 'Buy' ? 'is-profit' : 'is-loss'">{{ row.side === 'Buy' ? '买入' : '卖出' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="成交价" width="92" align="right">
              <template #default="{ row }"><span class="num">{{ fmtPrice(row.avgFillPrice ?? row.price) }}</span></template>
            </el-table-column>
            <el-table-column label="数量" width="86" align="right">
              <template #default="{ row }"><span class="num">{{ fmtNumber(row.filledQuantity ?? row.quantity, 4) }}</span></template>
            </el-table-column>
            <el-table-column label="状态" min-width="80">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" :type="orderStatusMeta(row.status).type">
                  {{ orderStatusMeta(row.status).label }}
                </el-tag>
              </template>
            </el-table-column>
            <template #empty>
              <EmptyState compact icon="Tickets" title="暂无订单" />
            </template>
          </el-table>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import EmptyState from '@/components/EmptyState.vue'
import { dashboardApi, marketApi, orderApi, strategyApi, taskApi, normalizePaged } from '@/api'
import {
  BAR_OPTIONS,
  DEFAULT_INSTRUMENTS,
  TASK_STATUS_OPTIONS,
  barLabel,
  modeLabel,
  orderStatusMeta,
  signalMeta,
  taskStatusMeta
} from '@/utils/constants'
import { fmtDateTime, fmtMoney, fmtNumber, fmtPct, fmtPrice, pnlClass } from '@/utils/format'

const route = useRoute()

const items = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(10)
const statusFilter = ref('')
const loading = ref(false)
const error = ref('')
const stats = ref({})
const actionLoading = ref(null)

const strategies = ref([])
const instruments = ref(DEFAULT_INSTRUMENTS.map((item) => ({ ...item })))

const dialogVisible = ref(false)
const dialogMode = ref('create')
const dialogFormRef = ref(null)
const submitting = ref(false)
const formError = ref('')
const editingId = ref(null)

const drawerVisible = ref(false)
const detailLoading = ref(false)
const detail = ref(null)
const taskOrders = ref([])

let pollTimer = null

function emptyForm() {
  return {
    name: '',
    strategyCode: '',
    instId: 'BTC-USDT',
    bar: '1H',
    mode: 'Live',
    allocatedCapital: 100,
    leverage: 1,
    slippagePct: 0.05,
    params: {},
    risk: { stopLossPct: 0, takeProfitPct: 0, maxPositionPct: 100 },
    startImmediately: true,
    liveRiskConfirmed: false
  }
}

const form = reactive(emptyForm())

const formRules = {
  name: [{ required: true, message: '请输入任务名称', trigger: 'blur' }],
  strategyCode: [{ required: true, message: '请选择策略', trigger: 'change' }],
  instId: [{ required: true, message: '请选择交易对', trigger: 'change' }],
  bar: [{ required: true, message: '请选择 K 线周期', trigger: 'change' }],
  allocatedCapital: [
    {
      validator(_rule, value, callback) {
        const num = Number(value)
        if (!Number.isFinite(num) || num < 10) callback(new Error('分配资金不能小于 10 USDT'))
        else callback()
      },
      trigger: 'change'
    }
  ],
  leverage: [
    {
      validator(_rule, value, callback) {
        const num = Number(value)
        if (!Number.isFinite(num) || num < 1 || num > 20) callback(new Error('杠杆需在 1–20 倍之间'))
        else callback()
      },
      trigger: 'change'
    }
  ],
  slippagePct: [
    {
      validator(_rule, value, callback) {
        const num = Number(value)
        if (!Number.isFinite(num) || num < 0 || num > 5) callback(new Error('滑点需在 0–5% 之间'))
        else callback()
      },
      trigger: 'change'
    }
  ]
}

const selectedStrategy = computed(
  () => strategies.value.find((item) => item.code === form.strategyCode) || null
)
const strategySchema = computed(() => selectedStrategy.value?.paramsSchema || [])

const paramEntries = computed(() => {
  const params = detail.value?.params || {}
  return Object.entries(params).map(([key, value]) => ({
    key,
    value: typeof value === 'boolean' ? (value ? '开启' : '关闭') : String(value ?? '—')
  }))
})

function applyStrategyDefaults(strategy, params) {
  const next = {}
  for (const param of strategy?.paramsSchema || []) {
    if (params && params[param.code] !== undefined) next[param.code] = params[param.code]
    else {
      const fallback = strategy?.defaultParams?.[param.code]
      next[param.code] = fallback !== undefined ? fallback : param.default
    }
  }
  form.params = next
}

function onStrategyChange(code) {
  const strategy = strategies.value.find((item) => item.code === code)
  applyStrategyDefaults(strategy)
  if (!form.name) form.name = `${strategy?.name || '策略'} 任务`
}

async function loadStats() {
  try {
    const res = await dashboardApi.overview()
    stats.value = res?.stats || {}
  } catch {
    // 统计信息失败不影响任务列表
  }
}

async function loadTasks() {
  loading.value = true
  try {
    const res = await taskApi.list({
      page: page.value,
      pageSize: pageSize.value,
      status: statusFilter.value
    })
    const paged = normalizePaged(res, page.value, pageSize.value)
    items.value = paged.items
    total.value = paged.total
    error.value = ''
  } catch (err) {
    error.value = err?.message || '任务列表加载失败'
  } finally {
    loading.value = false
  }
}

function onFilterChange() {
  page.value = 1
  loadTasks()
}

async function loadStrategies() {
  try {
    const res = await strategyApi.list()
    strategies.value = Array.isArray(res) ? res : res?.items || []
  } catch (err) {
    ElMessage.error(err?.message || '策略列表加载失败')
  }
}

async function loadInstruments() {
  try {
    const res = await marketApi.instruments()
    if (Array.isArray(res) && res.length) instruments.value = res
  } catch {
    // 使用默认交易对
  }
}

function openCreate() {
  dialogMode.value = 'create'
  editingId.value = null
  formError.value = ''
  Object.assign(form, emptyForm())
  const fromQuery = typeof route.query.strategy === 'string' ? route.query.strategy : ''
  const strategy = strategies.value.find((item) => item.code === fromQuery) || strategies.value[0]
  if (strategy) {
    form.strategyCode = strategy.code
    form.name = `${strategy.name} 任务`
    applyStrategyDefaults(strategy)
  }
  dialogVisible.value = true
  dialogFormRef.value?.clearValidate()
}

function openEdit(row) {
  if (row.status !== 'Stopped' && row.status !== 'Error') {
    ElMessage.warning('任务运行中，请先停止任务后再编辑')
    return
  }
  dialogMode.value = 'edit'
  editingId.value = row.id
  formError.value = ''
  Object.assign(form, emptyForm())
  form.name = row.name
  form.strategyCode = row.strategyCode
  form.instId = row.instId
  form.bar = row.bar
  form.mode = row.mode
  form.allocatedCapital = Number(row.allocatedCapital ?? 100)
  form.leverage = Math.max(1, Number(row.leverage || 1))
  form.slippagePct = Number(row.slippagePct ?? 0.05)
  form.risk = {
    stopLossPct: Number(row.risk?.stopLossPct ?? 0),
    takeProfitPct: Number(row.risk?.takeProfitPct ?? 0),
    maxPositionPct: Number(row.risk?.maxPositionPct ?? 100)
  }
  applyStrategyDefaults(selectedStrategy.value || strategies.value.find((s) => s.code === row.strategyCode), row.params)
  dialogVisible.value = true
  dialogFormRef.value?.clearValidate()
}

async function submitForm() {
  if (!dialogFormRef.value) return
  try {
    await dialogFormRef.value.validate()
  } catch {
    return
  }
  if (dialogMode.value === 'create' && form.mode === 'Live' && !form.liveRiskConfirmed) {
    formError.value = '请先勾选实盘交易风险确认'
    return
  }
  submitting.value = true
  formError.value = ''
  const payload = {
    name: form.name.trim(),
    strategyCode: form.strategyCode,
    instId: form.instId,
    bar: form.bar,
    allocatedCapital: Number(form.allocatedCapital),
    leverage: Number(form.leverage),
    slippagePct: Number(form.slippagePct),
    params: { ...form.params },
    risk: {
      stopLossPct: Number(form.risk.stopLossPct),
      takeProfitPct: Number(form.risk.takeProfitPct),
      maxPositionPct: Number(form.risk.maxPositionPct)
    }
  }
  try {
    if (dialogMode.value === 'create') {
      const res = await taskApi.create({
        ...payload,
        mode: form.mode,
        startImmediately: Boolean(form.startImmediately)
      })
      ElMessage.success(res?.message || '任务创建成功')
    } else {
      const res = await taskApi.update(editingId.value, payload)
      ElMessage.success(res?.message || '任务修改成功')
    }
    dialogVisible.value = false
    await Promise.all([loadTasks(), loadStats()])
  } catch (err) {
    formError.value = err?.message || '任务保存失败'
  } finally {
    submitting.value = false
  }
}

async function startTask(row) {
  try {
    await ElMessageBox.confirm(
      `确认启动任务「${row.name}」？${row.mode === 'Live' ? '该任务为实盘模式，将使用真实资金。' : ''}`,
      '启动任务',
      { type: 'warning', confirmButtonText: '启动', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  actionLoading.value = row.id
  try {
    await taskApi.start(row.id)
    ElMessage.success('任务已启动')
    await Promise.all([loadTasks(), loadStats()])
    if (drawerVisible.value && detail.value?.id === row.id) loadDetail(row.id)
  } catch (err) {
    ElMessage.error(err?.message || '启动任务失败')
  } finally {
    actionLoading.value = null
  }
}

async function closePosition(row) {
  try {
    await ElMessageBox.confirm(
      `确认按当前市价平掉任务「${row.name}」的持仓？${row.mode === 'Live' ? '实盘模式会向欧易提交 reduceOnly 平仓单。' : ''}`,
      '平仓确认',
      { type: 'warning', confirmButtonText: '平仓', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  actionLoading.value = row.id
  try {
    await taskApi.closePosition(row.id)
    ElMessage.success('持仓已平仓')
    await Promise.all([loadTasks(), loadStats()])
    if (drawerVisible.value && detail.value?.id === row.id) loadDetail(row.id)
  } catch (err) {
    ElMessage.error(err?.message || '平仓失败')
  } finally {
    actionLoading.value = null
  }
}

async function stopTask(row) {
  try {
    await ElMessageBox.confirm(`确认停止任务「${row.name}」？`, '停止任务', {
      type: 'warning',
      confirmButtonText: '停止',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }
  actionLoading.value = row.id
  try {
    await taskApi.stop(row.id)
    ElMessage.success('任务已停止')
    await Promise.all([loadTasks(), loadStats()])
    if (drawerVisible.value && detail.value?.id === row.id) loadDetail(row.id)
  } catch (err) {
    ElMessage.error(err?.message || '停止任务失败')
  } finally {
    actionLoading.value = null
  }
}

async function deleteTask(row) {
  try {
    await ElMessageBox.confirm(`删除后不可恢复，确认删除任务「${row.name}」？`, '删除任务', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }
  actionLoading.value = row.id
  try {
    await taskApi.remove(row.id)
    ElMessage.success('任务已删除')
    if (items.value.length === 1 && page.value > 1) page.value -= 1
    await Promise.all([loadTasks(), loadStats()])
  } catch (err) {
    ElMessage.error(err?.message || '删除任务失败')
  } finally {
    actionLoading.value = null
  }
}

async function loadDetail(id) {
  detailLoading.value = true
  try {
    const res = await taskApi.detail(id)
    detail.value = res || null
    try {
      const orderRes = await orderApi.list({ taskId: id, page: 1, pageSize: 5 })
      taskOrders.value = normalizePaged(orderRes, 1, 5).items
    } catch {
      taskOrders.value = []
    }
  } catch (err) {
    ElMessage.error(err?.message || '任务详情加载失败')
  } finally {
    detailLoading.value = false
  }
}

function openDetail(row) {
  drawerVisible.value = true
  detail.value = { ...row }
  taskOrders.value = []
  loadDetail(row.id)
}

function startPolling() {
  pollTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    loadTasks()
    loadStats()
  }, 10000)
}

onMounted(async () => {
  await Promise.all([loadStrategies(), loadInstruments()])
  loadTasks()
  loadStats()
  startPolling()
  if (route.query.create === '1') {
    openCreate()
  }
})

onBeforeUnmount(() => {
  if (pollTimer) clearInterval(pollTimer)
})
</script>

<style scoped>
.task-chips {
  display: flex;
  align-items: center;
  gap: 9px;
  flex-wrap: wrap;
}

.task-chip {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  height: 30px;
  padding: 0 12px;
  border-radius: 9px;
  border: 1px solid rgba(148, 163, 184, 0.12);
  background: rgba(148, 163, 184, 0.035);
  font-size: 11.5px;
  color: var(--qts-text-dim);
}

.task-chip b {
  font-size: 13px;
  color: var(--qts-text);
  font-weight: 600;
}

.task-chip--success {
  border-color: rgba(52, 211, 153, 0.22);
  background: rgba(52, 211, 153, 0.06);
}

.task-chip--success b {
  color: #6ee7b7;
}

.task-chip--danger {
  border-color: rgba(251, 113, 133, 0.22);
  background: rgba(251, 113, 133, 0.06);
}

.task-chip--danger b {
  color: #fda4af;
}

.task-chip--muted {
  margin-left: auto;
  border-style: dashed;
  background: transparent;
  color: rgba(100, 116, 139, 0.85);
}

.task-filters {
  display: flex;
  align-items: center;
  gap: 8px;
}

.task-name-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.task-name {
  font-size: 13px;
  font-weight: 600;
  color: var(--qts-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.task-strategy {
  font-size: 11px;
  color: var(--qts-text-dim);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.task-status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--qts-text-muted);
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--qts-text-dim);
}

.task-status.is-running {
  color: #6ee7b7;
}

.task-status.is-running .status-dot {
  background: var(--qts-success);
  box-shadow: 0 0 0 3px rgba(52, 211, 153, 0.14);
  animation: dot-pulse 1.8s ease-in-out infinite;
}

.task-status.is-stopped {
  color: var(--qts-text-dim);
}

.task-status.is-paused .status-dot {
  background: var(--qts-warning);
}

.task-status.is-error {
  color: #fda4af;
}

.task-status.is-error .status-dot {
  background: var(--qts-danger);
}

.task-form :deep(.el-form-item) {
  margin-bottom: 14px;
}

.task-form :deep(.el-form-item__label) {
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

.risk-tip {
  font-size: 11px;
  color: var(--qts-text-dim);
  margin-bottom: 12px;
}

.lev-cell {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 1px;
  line-height: 1.3;
}

.lev-cell b {
  font-weight: 600;
}

.lev-cell span {
  font-size: 10.5px;
  color: var(--qts-text-dim);
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

.live-confirm {
  margin: 12px 0 4px;
}

.form-alert {
  margin-top: 14px;
}

/* 抽屉 */
.drawer-loading {
  padding: 6px;
}

.drawer-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding-bottom: 16px;
  border-bottom: 1px solid rgba(148, 163, 184, 0.09);
}

.drawer-title {
  font-size: 17px;
  font-weight: 700;
  color: #f1f5f9;
}

.drawer-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 9px;
}

.drawer-actions {
  flex-shrink: 0;
}

.drawer-metrics {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  margin: 16px 0;
}

.drawer-metric {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 11px 13px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.1);
  background: rgba(148, 163, 184, 0.03);
}

.drawer-metric span {
  font-size: 11px;
  color: var(--qts-text-dim);
}

.drawer-metric b {
  font-size: 15px;
  color: var(--qts-text);
  font-weight: 600;
}

.drawer-desc {
  margin-bottom: 14px;
}

.drawer-alert {
  margin-bottom: 16px;
}

.drawer-section {
  margin-top: 20px;
}

.drawer-section-title {
  font-size: 12px;
  font-weight: 600;
  color: #cbd5e1;
  letter-spacing: 0.5px;
  margin-bottom: 10px;
}

.param-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.param-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 23px;
  padding: 0 8px;
  border-radius: 6px;
  background: rgba(148, 163, 184, 0.06);
  border: 1px solid rgba(148, 163, 184, 0.09);
  font-size: 11px;
}

.param-chip em {
  font-style: normal;
  color: var(--qts-text-dim);
}

.param-chip b {
  color: #cbd5e1;
  font-weight: 600;
  font-family: var(--qts-mono);
}

@media (max-width: 900px) {
  .task-chip--muted {
    margin-left: 0;
  }
}
</style>
