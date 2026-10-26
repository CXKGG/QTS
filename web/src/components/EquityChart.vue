<template>
  <div ref="chartEl" class="chart-host" :style="{ height: `${height}px` }"></div>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import * as echarts from 'echarts'
import { fmtChartTime, fmtMoney, fmtPct } from '@/utils/format'

const props = defineProps({
  points: { type: Array, default: () => [] },
  showDrawdown: { type: Boolean, default: false },
  height: { type: Number, default: 300 }
})

const chartEl = ref(null)
const chart = shallowRef(null)
let resizeHandler = null

const AXIS_LABEL = { color: '#8fa3bf', fontSize: 11 }

function render() {
  if (!chartEl.value || chartEl.value.clientWidth === 0) return
  if (!chart.value) {
    chart.value = echarts.init(chartEl.value, null, { renderer: 'canvas' })
  }
  chart.value.setOption(buildOption(), true)
}

function buildOption() {
  const points = props.points || []
  const categories = points.map((point) => fmtChartTime(point.time))
  const equityData = points.map((point) => Number(point.equity ?? 0))
  const hasDrawdown =
    props.showDrawdown &&
    points.some((point) => point.drawdownPct !== null && point.drawdownPct !== undefined)
  const drawdownData = points.map((point) => -Math.abs(Number(point.drawdownPct ?? 0)))

  const series = [
    {
      name: '权益',
      type: 'line',
      data: equityData,
      showSymbol: false,
      smooth: 0.18,
      symbolSize: 5,
      lineStyle: { width: 2, color: '#22d3ee' },
      itemStyle: { color: '#22d3ee' },
      areaStyle: {
        color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
          { offset: 0, color: 'rgba(34, 211, 238, 0.30)' },
          { offset: 1, color: 'rgba(34, 211, 238, 0.01)' }
        ])
      },
      yAxisIndex: 0,
      z: 3
    }
  ]

  if (hasDrawdown) {
    series.push({
      name: '回撤',
      type: 'line',
      data: drawdownData,
      showSymbol: false,
      smooth: 0.2,
      lineStyle: { width: 1.3, color: '#fb7185' },
      itemStyle: { color: '#fb7185' },
      areaStyle: { color: 'rgba(251, 113, 133, 0.16)' },
      yAxisIndex: 1,
      z: 2
    })
  }

  const yAxis = [
    {
      type: 'value',
      scale: true,
      position: 'left',
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: { ...AXIS_LABEL, formatter: (value) => fmtMoney(value, 0) },
      splitLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.08)' } }
    }
  ]

  if (hasDrawdown) {
    yAxis.push({
      type: 'value',
      scale: true,
      max: 0,
      position: 'right',
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: {
        ...AXIS_LABEL,
        formatter: (value) => `${Math.abs(Number(value)).toFixed(1)}%`
      },
      splitLine: { show: false }
    })
  }

  return {
    animationDuration: 500,
    grid: {
      left: 6,
      right: 6,
      top: hasDrawdown ? 30 : 18,
      bottom: 4,
      containLabel: true
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'rgba(11, 18, 32, 0.96)',
      borderColor: 'rgba(148, 163, 184, 0.22)',
      borderWidth: 1,
      padding: [8, 12],
      textStyle: { color: '#e2e8f0', fontSize: 12 },
      axisPointer: {
        type: 'line',
        lineStyle: { color: 'rgba(34, 211, 238, 0.45)', width: 1, type: 'dashed' }
      },
      formatter: (params) => {
        const list = Array.isArray(params) ? params : [params]
        if (!list.length) return ''
        const rows = list.map((item) => {
          const isDrawdown = item.seriesName === '回撤'
          const value = isDrawdown
            ? fmtPct(Number(item.value ?? 0))
            : fmtMoney(Number(item.value ?? 0))
          return `<div style="display:flex;align-items:center;gap:8px;margin-top:3px">${item.marker}<span style="color:#93a6bf">${item.seriesName}</span><b style="margin-left:auto;font-family:monospace">${value}</b></div>`
        })
        return `<div style="font-family:monospace;color:#93a6bf;font-size:11.5px">${list[0].axisValueLabel}</div>${rows.join('')}`
      }
    },
    legend: hasDrawdown
      ? {
          show: true,
          data: ['权益', '回撤'],
          right: 10,
          top: 0,
          itemWidth: 10,
          itemHeight: 8,
          textStyle: { color: '#93a6bf', fontSize: 11 }
        }
      : { show: false },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: categories,
      axisLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.16)' } },
      axisTick: { show: false },
      axisLabel: { ...AXIS_LABEL, hideOverlap: true }
    },
    yAxis,
    series
  }
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
  () => [props.points, props.showDrawdown],
  () => render(),
  { deep: true }
)
</script>
