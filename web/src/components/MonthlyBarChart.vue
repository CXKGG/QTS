<template>
  <div ref="chartEl" class="chart-host" :style="{ height: `${height}px` }"></div>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import * as echarts from 'echarts'
import { fmtPct } from '@/utils/format'

const props = defineProps({
  data: { type: Array, default: () => [] },
  height: { type: Number, default: 220 }
})

const chartEl = ref(null)
const chart = shallowRef(null)
let resizeHandler = null

const AXIS_LABEL = { color: '#8fa3bf', fontSize: 11 }

function buildOption() {
  const items = props.data || []
  const categories = items.map((item) => item.month)
  const values = items.map((item) => Number(item.returnPct ?? 0))

  return {
    animationDuration: 450,
    grid: { left: 6, right: 10, top: 18, bottom: 4, containLabel: true },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'rgba(11, 18, 32, 0.96)',
      borderColor: 'rgba(148, 163, 184, 0.22)',
      borderWidth: 1,
      padding: [8, 12],
      textStyle: { color: '#e2e8f0', fontSize: 12 },
      axisPointer: { type: 'shadow', shadowStyle: { color: 'rgba(148, 163, 184, 0.06)' } },
      formatter: (params) => {
        const list = Array.isArray(params) ? params : [params]
        const item = list[0]
        if (!item) return ''
        const value = Number(item.value ?? 0)
        const color = value >= 0 ? '#34d399' : '#fb7185'
        return `<div style="font-family:monospace;color:#93a6bf;font-size:11.5px;margin-bottom:3px">${item.axisValue}</div><div style="font-family:monospace;color:${color};font-weight:600">${fmtPct(value)}</div>`
      }
    },
    xAxis: {
      type: 'category',
      data: categories,
      axisLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.16)' } },
      axisTick: { show: false },
      axisLabel: {
        ...AXIS_LABEL,
        formatter: (value) => String(value).replace(/^\d{4}-/, '')
      }
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: { ...AXIS_LABEL, formatter: (value) => `${Number(value).toFixed(0)}%` },
      splitLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.08)' } }
    },
    series: [
      {
        name: '月度收益',
        type: 'bar',
        data: values,
        barMaxWidth: 26,
        itemStyle: {
          borderRadius: [3, 3, 0, 0],
          color: (params) =>
            Number(params.value) >= 0 ? 'rgba(52, 211, 153, 0.78)' : 'rgba(251, 113, 133, 0.78)'
        }
      }
    ]
  }
}

function render() {
  if (!chartEl.value || chartEl.value.clientWidth === 0) return
  if (!chart.value) {
    chart.value = echarts.init(chartEl.value, null, { renderer: 'canvas' })
  }
  chart.value.setOption(buildOption(), true)
}

function handleResize() {
  if (chart.value) {
    chart.value.resize()
  } else {
    render()
  }
}

onMounted(() => {
  nextTick(() => {
    render()
    resizeHandler = handleResize
    window.addEventListener('resize', resizeHandler)
  })
})

onBeforeUnmount(() => {
  if (resizeHandler) window.removeEventListener('resize', resizeHandler)
  if (chart.value) {
    chart.value.dispose()
    chart.value = null
  }
})

watch(
  () => props.data,
  () => render(),
  { deep: true }
)
</script>
