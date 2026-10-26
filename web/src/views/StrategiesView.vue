<template>
  <div class="page-stack">
    <PageHeader title="策略库" subtitle="后端注册的量化策略与可配置参数">
      <template #tag>
        <el-tag size="small" effect="plain" type="info">{{ strategies.length }} 个策略</el-tag>
      </template>
      <el-button :icon="Refresh" :loading="loading" @click="loadStrategies">刷新</el-button>
    </PageHeader>

    <el-alert
      type="info"
      :closable="false"
      show-icon
      title="策略由后端统一注册与执行"
      description="回测与实盘任务使用同一套策略参数，策略参数的默认值会自动带入回测与任务创建表单。"
    />
    <el-alert v-if="error" type="error" :title="error" show-icon :closable="false" />

    <div v-if="loading && !strategies.length" class="strategy-grid">
      <div v-for="index in 4" :key="index" class="qts-card strategy-skeleton">
        <el-skeleton :rows="5" animated />
      </div>
    </div>

    <EmptyState
      v-else-if="!strategies.length"
      icon="Cpu"
      title="暂无可用策略"
      description="请确认后端策略注册是否正常，或稍后刷新重试。"
    >
      <el-button size="small" :loading="loading" @click="loadStrategies">重新加载</el-button>
    </EmptyState>

    <div v-else class="strategy-grid">
      <article
        v-for="strategy in strategies"
        :key="strategy.code"
        class="qts-card qts-card--lift strategy-card"
        :style="{ '--cat-color': categoryColor(strategy.category) }"
      >
        <div class="strategy-top">
          <span class="strategy-icon">
            <el-icon><component :is="categoryIcon(strategy.category)" /></el-icon>
          </span>
          <div class="strategy-title">
            <h3>{{ strategy.name }}</h3>
            <div class="strategy-meta">
              <span class="strategy-code">{{ strategy.code }}</span>
              <span class="strategy-category">{{ strategy.category || '通用' }}</span>
            </div>
          </div>
        </div>

        <p class="strategy-desc">{{ strategy.description || '该策略暂未提供说明。' }}</p>

        <div class="strategy-params">
          <span v-for="chip in paramChips(strategy)" :key="chip.label" class="param-chip">
            <em>{{ chip.label }}</em>
            <b>{{ chip.value }}</b>
          </span>
          <span v-if="!paramChips(strategy).length" class="text-dim strategy-no-param">无可配置参数</span>
        </div>

        <div class="strategy-actions">
          <el-button size="small" :icon="DataAnalysis" @click="goBacktest(strategy.code)">去回测</el-button>
          <el-button size="small" type="primary" plain :icon="Plus" @click="goTask(strategy.code)">创建任务</el-button>
        </div>
      </article>
    </div>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { DataAnalysis, Plus, Refresh } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import EmptyState from '@/components/EmptyState.vue'
import { strategyApi } from '@/api'
import { categoryColor, categoryIcon } from '@/utils/constants'

const router = useRouter()
const loading = ref(false)
const error = ref('')
const strategies = ref([])

async function loadStrategies() {
  loading.value = true
  try {
    const res = await strategyApi.list()
    strategies.value = Array.isArray(res) ? res : res?.items || []
    error.value = ''
  } catch (err) {
    error.value = err?.message || '策略列表加载失败'
  } finally {
    loading.value = false
  }
}

function displayParamValue(param) {
  const value = param?.default
  if (value === null || value === undefined || value === '') return '—'
  if (param.type === 'bool') return value ? '开启' : '关闭'
  if (param.type === 'select') {
    const matched = (param.options || []).find((option) => option.value === value)
    return matched?.label ?? String(value)
  }
  return String(value)
}

function paramChips(strategy) {
  return (strategy.paramsSchema || []).slice(0, 6).map((param) => ({
    label: param.label || param.code,
    value: displayParamValue(param)
  }))
}

function goBacktest(code) {
  router.push({ path: '/backtest', query: { strategy: code } })
}

function goTask(code) {
  router.push({ path: '/tasks', query: { strategy: code, create: '1' } })
}

onMounted(loadStrategies)
</script>

<style scoped>
.strategy-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(330px, 1fr));
  gap: 16px;
}

.strategy-skeleton {
  padding: 22px;
}

.strategy-card {
  display: flex;
  flex-direction: column;
  padding: 20px 20px 16px;
  position: relative;
  overflow: hidden;
}

.strategy-card::before {
  content: '';
  position: absolute;
  top: 0;
  left: 20px;
  right: 20px;
  height: 2px;
  border-radius: 0 0 3px 3px;
  background: linear-gradient(90deg, var(--cat-color, #22d3ee), transparent 88%);
  opacity: 0.85;
}

.strategy-card::after {
  content: '';
  position: absolute;
  top: -60px;
  right: -60px;
  width: 170px;
  height: 170px;
  border-radius: 50%;
  background: radial-gradient(circle, color-mix(in srgb, var(--cat-color, #22d3ee) 14%, transparent), transparent 70%);
  pointer-events: none;
}

.strategy-top {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  position: relative;
  z-index: 1;
}

.strategy-icon {
  display: grid;
  place-items: center;
  width: 40px;
  height: 40px;
  flex-shrink: 0;
  border-radius: 12px;
  font-size: 19px;
  color: var(--cat-color, #22d3ee);
  background: color-mix(in srgb, var(--cat-color, #22d3ee) 12%, transparent);
  border: 1px solid color-mix(in srgb, var(--cat-color, #22d3ee) 30%, transparent);
}

.strategy-title {
  min-width: 0;
}

.strategy-title h3 {
  font-size: 16px;
  font-weight: 700;
  color: #f1f5f9;
  letter-spacing: 0.3px;
}

.strategy-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 7px;
  margin-top: 5px;
}

.strategy-code {
  font-family: var(--qts-mono);
  font-size: 10.5px;
  color: var(--qts-text-dim);
  padding: 1px 6px;
  border-radius: 4px;
  border: 1px solid rgba(148, 163, 184, 0.14);
  background: rgba(148, 163, 184, 0.05);
}

.strategy-category {
  font-size: 11px;
  color: var(--cat-color, #22d3ee);
  letter-spacing: 0.5px;
}

.strategy-desc {
  margin-top: 13px;
  font-size: 12.5px;
  line-height: 1.75;
  color: var(--qts-text-muted);
  min-height: 44px;
  flex: 1;
}

.strategy-params {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 14px;
  padding-top: 13px;
  border-top: 1px solid rgba(148, 163, 184, 0.07);
}

.param-chip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  height: 23px;
  padding: 0 8px;
  border-radius: 6px;
  background: rgba(148, 163, 184, 0.06);
  border: 1px solid rgba(148, 163, 184, 0.09);
  font-size: 11px;
  max-width: 100%;
}

.param-chip em {
  font-style: normal;
  color: var(--qts-text-dim);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.param-chip b {
  color: #cbd5e1;
  font-weight: 600;
  font-family: var(--qts-mono);
  white-space: nowrap;
}

.strategy-no-param {
  font-size: 11px;
}

.strategy-actions {
  display: flex;
  gap: 8px;
  margin-top: 16px;
}

.strategy-actions .el-button {
  flex: 1;
}

@media (max-width: 560px) {
  .strategy-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
