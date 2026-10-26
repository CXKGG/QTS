<template>
  <div class="stat-card qts-card qts-card--lift" :class="`accent-${accent}`">
    <div class="stat-head">
      <span class="stat-label">{{ label }}</span>
      <span class="stat-icon">
        <el-icon><component :is="icon" /></el-icon>
      </span>
    </div>
    <div class="stat-value num" :style="valueColor ? { color: valueColor } : null">
      {{ displayValue }}
      <span v-if="unit" class="stat-unit">{{ unit }}</span>
    </div>
    <div class="stat-foot">
      <span v-if="trend !== null && trend !== undefined" class="stat-trend num" :class="trend >= 0 ? 'is-up' : 'is-down'">
        <el-icon><component :is="trend >= 0 ? 'CaretTop' : 'CaretBottom'" /></el-icon>
        {{ fmtPct(trend) }}
      </span>
      <span class="stat-sub">{{ sub }}</span>
    </div>
    <Sparkline v-if="spark && spark.length > 1" class="stat-spark" :values="spark" :color="sparkColor" />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import Sparkline from './Sparkline.vue'
import { fmtMoney, fmtPct } from '@/utils/format'

const props = defineProps({
  label: { type: String, required: true },
  value: { type: [String, Number], default: null },
  unit: { type: String, default: '' },
  sub: { type: String, default: '' },
  trend: { type: Number, default: null },
  icon: { type: String, default: 'DataLine' },
  accent: { type: String, default: 'cyan' },
  valueColor: { type: String, default: '' },
  spark: { type: Array, default: () => [] },
  digits: { type: Number, default: 2 }
})

const displayValue = computed(() => {
  if (props.value === null || props.value === undefined || props.value === '') return '—'
  if (typeof props.value === 'string') return props.value
  return fmtMoney(props.value, props.digits)
})

const sparkColor = computed(() => {
  const map = {
    cyan: '#22d3ee',
    green: '#34d399',
    red: '#fb7185',
    amber: '#fbbf24',
    violet: '#a78bfa',
    blue: '#60a5fa'
  }
  return map[props.accent] || '#22d3ee'
})
</script>
