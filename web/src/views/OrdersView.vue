<template>
  <div class="page-stack">
    <PageHeader title="订单与持仓" subtitle="策略任务产生的成交记录与当前持仓">
      <template #tag>
        <el-tag size="small" effect="plain" type="info">{{ positions.length }} 个持仓 · {{ total }} 条订单</el-tag>
      </template>
      <el-button :icon="Refresh" :loading="refreshing" @click="refreshAll">刷新</el-button>
    </PageHeader>

    <section class="qts-card orders-panel">
      <el-tabs v-model="activeTab" class="orders-tabs" @tab-change="onTabChange">
        <el-tab-pane name="orders">
          <template #label>
            <span class="tab-label">订单流水 <em>{{ total }}</em></span>
          </template>

          <div class="tab-toolbar">
            <div class="tab-filters">
              <el-select v-model="modeFilter" size="small" style="width: 120px" @change="onFilterChange">
                <el-option label="全部模式" value="" />
                <el-option label="模拟盘" value="Paper" />
                <el-option label="实盘" value="Live" />
              </el-select>
              <el-select v-model="statusFilter" size="small" style="width: 130px" @change="onFilterChange">
                <el-option v-for="item in ORDER_STATUS_OPTIONS" :key="item.value" :label="item.label" :value="item.value" />
              </el-select>
              <span class="text-dim toolbar-tip">每 15 秒自动刷新</span>
            </div>
            <el-button size="small" :icon="Refresh" :loading="ordersLoading" @click="loadOrders">刷新订单</el-button>
          </div>

          <el-alert v-if="ordersError" type="error" :title="ordersError" show-icon :closable="false" class="tab-alert" />

          <el-table v-loading="ordersLoading" :data="orders" size="small" style="width: 100%">
            <el-table-column label="时间" width="150">
              <template #default="{ row }"><span class="num text-muted">{{ fmtDateTime(row.createdAt) }}</span></template>
            </el-table-column>
            <el-table-column label="任务" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">
                <span>{{ row.taskName || `任务 #${row.taskId}` }}</span>
              </template>
            </el-table-column>
            <el-table-column label="交易对" width="110">
              <template #default="{ row }"><span class="mono">{{ row.instId }}</span></template>
            </el-table-column>
            <el-table-column label="方向" width="78">
              <template #default="{ row }">
                <span class="side-tag" :class="row.side === 'Buy' ? 'is-buy' : 'is-sell'">
                  {{ row.side === 'Buy' ? '买入' : '卖出' }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="模式" width="84">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" :type="row.mode === 'Live' ? 'danger' : 'info'">
                  {{ modeLabel(row.mode) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="类型" width="76">
              <template #default="{ row }"><span class="text-muted">{{ orderTypeLabel(row.orderType) }}</span></template>
            </el-table-column>
            <el-table-column label="委托价" width="100" align="right">
              <template #default="{ row }"><span class="num">{{ fmtPrice(row.price) }}</span></template>
            </el-table-column>
            <el-table-column label="成交量" width="100" align="right">
              <template #default="{ row }"><span class="num">{{ fmtNumber(row.filledQuantity ?? row.quantity, 4) }}</span></template>
            </el-table-column>
            <el-table-column label="成交均价" width="104" align="right">
              <template #default="{ row }"><span class="num">{{ fmtPrice(row.avgFillPrice) }}</span></template>
            </el-table-column>
            <el-table-column label="手续费" width="90" align="right">
              <template #default="{ row }"><span class="num text-muted">{{ fmtMoney(row.fee) }}</span></template>
            </el-table-column>
            <el-table-column label="状态" width="96">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" :type="orderStatusMeta(row.status).type">
                  {{ orderStatusMeta(row.status).label }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="说明" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.error" class="order-error">{{ row.error }}</span>
                <span v-else-if="row.reason" class="text-muted">{{ row.reason }}</span>
                <span v-else class="text-dim">—</span>
              </template>
            </el-table-column>
            <el-table-column label="OKX 订单号" width="150">
              <template #default="{ row }">
                <span class="mono text-dim">{{ row.okxOrderId || '—' }}</span>
              </template>
            </el-table-column>
            <template #empty>
              <EmptyState
                compact
                icon="Tickets"
                title="暂无订单记录"
                description="策略任务运行并产生成交后，订单流水会展示在这里。"
              />
            </template>
          </el-table>

          <div v-if="total > pageSize" class="orders-pagination">
            <el-pagination
              v-model:current-page="page"
              :page-size="pageSize"
              :total="total"
              layout="total, prev, pager, next"
              background
              small
              @current-change="loadOrders"
            />
          </div>
        </el-tab-pane>

        <el-tab-pane name="positions">
          <template #label>
            <span class="tab-label">当前持仓 <em>{{ positions.length }}</em></span>
          </template>

          <div class="tab-toolbar">
            <div class="tab-filters">
              <span class="positions-summary">
                浮动盈亏合计
                <b class="num" :class="pnlClass(positionsPnl)">{{ fmtMoney(positionsPnl) }}</b>
                <span class="text-dim">USDT</span>
              </span>
              <span class="text-dim toolbar-tip">每 15 秒自动刷新</span>
            </div>
            <el-button size="small" :icon="Refresh" :loading="positionsLoading" @click="loadPositions">刷新持仓</el-button>
          </div>

          <el-alert v-if="positionsError" type="error" :title="positionsError" show-icon :closable="false" class="tab-alert" />

          <div v-if="positionsLoading && !positions.length" class="position-grid">
            <div v-for="index in 3" :key="index" class="qts-card position-card position-skeleton">
              <el-skeleton :rows="4" animated />
            </div>
          </div>

          <EmptyState
            v-else-if="!positions.length"
            icon="Wallet"
            title="当前没有持仓"
            description="策略任务产生开仓信号后，持仓会显示在这里；持仓随行情实时更新浮动盈亏。"
          />

          <div v-else class="position-grid">
            <article v-for="pos in positions" :key="pos.id" class="qts-card position-card">
              <div class="pos-top">
                <div class="pos-symbol-wrap">
                  <span class="pos-symbol">{{ pos.instId }}</span>
                  <el-tag size="small" effect="plain" :type="pos.mode === 'Live' ? 'danger' : 'info'">
                    {{ modeLabel(pos.mode) }}
                  </el-tag>
                  <el-tag size="small" effect="plain" type="success">多头</el-tag>
                </div>
                <div class="pos-pnl">
                  <b class="num" :class="pnlClass(pos.unrealizedPnl)">{{ fmtMoney(pos.unrealizedPnl) }}</b>
                  <span class="num" :class="pnlClass(pos.unrealizedPnlPct)">{{ fmtPct(pos.unrealizedPnlPct) }}</span>
                </div>
              </div>

              <div class="pos-grid">
                <div class="pos-item">
                  <span>持仓数量</span>
                  <b class="num">{{ fmtNumber(pos.quantity, 6) }}</b>
                </div>
                <div class="pos-item">
                  <span>开仓均价</span>
                  <b class="num">{{ fmtPrice(pos.avgEntryPrice) }}</b>
                </div>
                <div class="pos-item">
                  <span>最新价格</span>
                  <b class="num">{{ fmtPrice(pos.lastPrice) }}</b>
                </div>
                <div class="pos-item">
                  <span>止损价格</span>
                  <b class="num">{{ pos.stopLoss ? fmtPrice(pos.stopLoss) : '未设置' }}</b>
                </div>
                <div class="pos-item">
                  <span>止盈价格</span>
                  <b class="num">{{ pos.takeProfit ? fmtPrice(pos.takeProfit) : '未设置' }}</b>
                </div>
                <div class="pos-item">
                  <span>开仓时间</span>
                  <b class="num">{{ fmtDateTime(pos.openedAt) }}</b>
                </div>
              </div>

              <div class="pos-foot">
                <span>来源任务：{{ pos.taskName || `任务 #${pos.taskId}` }}</span>
                <span class="mono text-dim">ID {{ pos.id }}</span>
              </div>
            </article>
          </div>
        </el-tab-pane>
      </el-tabs>
    </section>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import EmptyState from '@/components/EmptyState.vue'
import { orderApi, normalizePaged } from '@/api'
import { ORDER_STATUS_OPTIONS, modeLabel, orderStatusMeta, orderTypeLabel } from '@/utils/constants'
import { fmtDateTime, fmtMoney, fmtNumber, fmtPct, fmtPrice, pnlClass } from '@/utils/format'

const route = useRoute()

const activeTab = ref(route.query.tab === 'positions' ? 'positions' : 'orders')
const positions = ref([])
const positionsLoading = ref(false)
const positionsError = ref('')
const orders = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(15)
const modeFilter = ref('')
const statusFilter = ref('')
const ordersLoading = ref(false)
const ordersError = ref('')
const refreshing = ref(false)

let pollTimer = null
let ordersLoaded = false
let positionsLoaded = false

const positionsPnl = computed(() =>
  positions.value.reduce((sum, item) => sum + Number(item.unrealizedPnl ?? 0), 0)
)

async function loadOrders() {
  ordersLoading.value = true
  try {
    const res = await orderApi.list({
      page: page.value,
      pageSize: pageSize.value,
      mode: modeFilter.value,
      status: statusFilter.value
    })
    const paged = normalizePaged(res, page.value, pageSize.value)
    orders.value = paged.items
    total.value = paged.total
    ordersError.value = ''
    ordersLoaded = true
  } catch (err) {
    ordersError.value = err?.message || '订单加载失败'
  } finally {
    ordersLoading.value = false
  }
}

async function loadPositions() {
  positionsLoading.value = true
  try {
    const res = await orderApi.positions({ status: 'open' })
    positions.value = Array.isArray(res) ? res : res?.items || []
    positionsError.value = ''
    positionsLoaded = true
  } catch (err) {
    positionsError.value = err?.message || '持仓加载失败'
  } finally {
    positionsLoading.value = false
  }
}

function onFilterChange() {
  page.value = 1
  loadOrders()
}

function onTabChange(name) {
  if (name === 'orders' && !ordersLoaded) loadOrders()
  if (name === 'positions' && !positionsLoaded) loadPositions()
}

async function refreshAll() {
  refreshing.value = true
  try {
    await Promise.all([
      loadOrders(),
      loadPositions()
    ])
  } finally {
    refreshing.value = false
  }
}

function loadActiveTab(silent = true) {
  if (activeTab.value === 'orders') loadOrders()
  else loadPositions()
  if (!silent) ElMessage.success('已刷新')
}

function startPolling() {
  pollTimer = setInterval(() => {
    if (document.visibilityState === 'hidden') return
    loadActiveTab()
  }, 15000)
}

onMounted(() => {
  if (activeTab.value === 'positions') loadPositions()
  else loadOrders()
  startPolling()
})

onBeforeUnmount(() => {
  if (pollTimer) clearInterval(pollTimer)
})
</script>

<style scoped>
.orders-panel {
  padding: 4px 0 0;
}

.orders-tabs :deep(.el-tabs__header) {
  margin: 0;
  padding: 0 20px;
}

.orders-tabs :deep(.el-tabs__content) {
  padding: 0 20px 18px;
}

.tab-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}

.tab-label em {
  font-style: normal;
  font-size: 10.5px;
  color: var(--qts-text-dim);
  padding: 0 6px;
  height: 17px;
  line-height: 17px;
  border-radius: 5px;
  background: rgba(148, 163, 184, 0.1);
}

.tab-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  padding: 12px 0 14px;
}

.tab-filters {
  display: flex;
  align-items: center;
  gap: 9px;
  flex-wrap: wrap;
}

.toolbar-tip {
  font-size: 11px;
}

.tab-alert {
  margin-bottom: 12px;
}

.orders-pagination {
  display: flex;
  justify-content: flex-end;
  padding-top: 14px;
}

.positions-summary {
  display: inline-flex;
  align-items: baseline;
  gap: 6px;
  font-size: 11.5px;
  color: var(--qts-text-dim);
}

.positions-summary b {
  font-size: 15px;
  color: var(--qts-text);
}

.mono {
  font-family: var(--qts-mono);
  font-size: 11.5px;
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

.order-error {
  color: #fda4af;
  font-size: 11.5px;
}

/* 持仓卡片 */
.position-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
  gap: 14px;
}

.position-card {
  padding: 16px 18px;
  display: flex;
  flex-direction: column;
}

.position-skeleton {
  padding: 20px;
}

.pos-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding-bottom: 12px;
  border-bottom: 1px solid rgba(148, 163, 184, 0.08);
}

.pos-symbol-wrap {
  display: flex;
  align-items: center;
  gap: 7px;
  flex-wrap: wrap;
}

.pos-symbol {
  font-size: 15px;
  font-weight: 700;
  color: #f1f5f9;
  letter-spacing: 0.3px;
  font-family: var(--qts-mono);
}

.pos-pnl {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
}

.pos-pnl b {
  font-size: 17px;
  font-weight: 700;
}

.pos-pnl span {
  font-size: 11.5px;
}

.pos-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px 18px;
  padding: 13px 0;
}

.pos-item {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.pos-item span {
  font-size: 10.5px;
  color: var(--qts-text-dim);
}

.pos-item b {
  font-size: 12.5px;
  color: var(--qts-text);
  font-weight: 600;
}

.pos-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-top: 10px;
  border-top: 1px solid rgba(148, 163, 184, 0.07);
  font-size: 11px;
  color: var(--qts-text-dim);
}

@media (max-width: 620px) {
  .position-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
