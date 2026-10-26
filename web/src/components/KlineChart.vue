<template>
  <div ref="chartEl" class="chart-host" :style="{ height: `${height}px` }"></div>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import * as echarts from 'echarts'
import { fmtChartTime, fmtCompact, fmtDateTime, fmtPct, fmtPrice } from '@/utils/format'
import { getKlineColors, hexToRgba } from '@/utils/chartColors'

const props = defineProps({
  candles: { type: Array, default: () => [] },
  height: { type: Number, default: 420 },
  /**
   * 初始可见 K 线数量。放大后组件会记住用户选择的柱数，
   * 新数据到达时保持同样的可见数量并锚定到最新一根。
   */
  visibleBars: { type: Number, default: 180 }
})

const chartEl = ref(null)
const chart = shallowRef(null)
const colors = ref(getKlineColors())
const visibleCount = ref(0)
let resizeHandler = null
let colorHandler = null
let applyingZoom = false

const AXIS_LABEL = { color: '#8fa3bf', fontSize: 11 }

function handleColorChange(event) {
  colors.value = { ...colors.value, ...(event?.detail || getKlineColors()) }
  render()
}

function handleDataZoom() {
  if (applyingZoom || !chart.value) return
  const option = chart.value.getOption()
  const zoom = Array.isArray(option.dataZoom) ? option.dataZoom[0] : null
  const total = (props.candles || []).length
  if (!zoom || !total) return
  const start = Number(zoom.start ?? 0)
  const end = Number(zoom.end ?? 100)
  if (end - start <= 0.01) return
  const count = Math.round((total * (end - start)) / 100)
  if (Number.isFinite(count)) {
    visibleCount.value = Math.max(10, Math.min(total, count))
  }
}

function calcMA(values, period) {
  const result = []
  let sum = 0
  for (let index = 0; index < values.length; index += 1) {
    const value = Number(values[index])
    sum += Number.isFinite(value) ? value : 0
    if (index >= period) {
      sum -= Number(values[index - period])
      result.push(Number((sum / period).toFixed(6)))
    } else if (index === period - 1) {
      result.push(Number((sum / period).toFixed(6)))
    } else {
      result.push(null)
    }
  }
  return result
}

function buildOption() {
  const candles = props.candles || []
  const categories = candles.map((item) => fmtChartTime(item.time))
  const klineData = candles.map((item) => [
    Number(item.open),
    Number(item.close),
    Number(item.low),
    Number(item.high)
  ])
  const closes = candles.map((item) => Number(item.close))
  const upColor = colors.value.up
  const downColor = colors.value.down
  const volumes = candles.map((item) => ({
    value: Number(item.volume ?? 0),
    itemStyle: {
      color:
        Number(item.close) >= Number(item.open)
          ? hexToRgba(upColor, 0.55)
          : hexToRgba(downColor, 0.55)
    }
  }))

  const total = candles.length
  const wanted = visibleCount.value || Math.max(10, props.visibleBars || 180)
  const visible = Math.max(10, Math.min(total || 10, wanted))
  const zoomStart = total > visible ? Math.max(0, ((total - visible) / total) * 100) : 0

  const series = [
    {
      name: 'K线',
      type: 'candlestick',
      data: klineData,
      xAxisIndex: 0,
      yAxisIndex: 0,
      itemStyle: {
        color: upColor,
        color0: downColor,
        borderColor: upColor,
        borderColor0: downColor
      }
    },
    {
      name: 'MA5',
      type: 'line',
      data: calcMA(closes, 5),
      showSymbol: false,
      smooth: true,
      lineStyle: { width: 1.2, color: '#fbbf24' },
      itemStyle: { color: '#fbbf24' },
      xAxisIndex: 0,
      yAxisIndex: 0,
      z: 4
    },
    {
      name: 'MA20',
      type: 'line',
      data: calcMA(closes, 20),
      showSymbol: false,
      smooth: true,
      lineStyle: { width: 1.2, color: '#a78bfa' },
      itemStyle: { color: '#a78bfa' },
      xAxisIndex: 0,
      yAxisIndex: 0,
      z: 4
    },
    {
      name: '成交量',
      type: 'bar',
      data: volumes,
      xAxisIndex: 1,
      yAxisIndex: 1,
      barWidth: '62%'
    }
  ]

  return {
    animation: false,
    axisPointer: {
      link: [{ xAxisIndex: 'all' }]
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'rgba(11, 18, 32, 0.96)',
      borderColor: 'rgba(148, 163, 184, 0.22)',
      borderWidth: 1,
      padding: [9, 12],
      textStyle: { color: '#e2e8f0', fontSize: 12 },
      axisPointer: {
        type: 'cross',
        crossStyle: { color: 'rgba(148, 163, 184, 0.35)' },
        label: { backgroundColor: '#111a2e', color: '#cbd5e1', fontSize: 11 }
      },
      formatter: (params) => {
        const list = Array.isArray(params) ? params : [params]
        const first = list.find((item) => item.seriesName === 'K线') || list[0]
        const index = first?.dataIndex
        const candle = candles[index]
        if (!candle) return ''
        const open = Number(candle.open)
        const close = Number(candle.close)
        const changePct = open ? ((close - open) / open) * 100 : 0
        const color = changePct >= 0 ? upColor : downColor
        return `
          <div style="font-family:monospace;color:#93a6bf;font-size:11.5px;margin-bottom:5px">${fmtDateTime(candle.time)}</div>
          <div style="display:grid;grid-template-columns:auto auto;gap:2px 18px;font-family:monospace;font-size:12px">
            <span style="color:#93a6bf">开盘</span><span>${fmtPrice(open)}</span>
            <span style="color:#93a6bf">最高</span><span>${fmtPrice(candle.high)}</span>
            <span style="color:#93a6bf">最低</span><span>${fmtPrice(candle.low)}</span>
            <span style="color:#93a6bf">收盘</span><span style="color:${color}">${fmtPrice(close)}</span>
            <span style="color:#93a6bf">涨跌</span><span style="color:${color}">${fmtPct(changePct)}</span>
            <span style="color:#93a6bf">成交量</span><span>${fmtCompact(candle.volume)}</span>
          </div>`
      }
    },
    legend: {
      show: true,
      data: ['MA5', 'MA20'],
      right: 14,
      top: 0,
      itemWidth: 12,
      itemHeight: 2,
      textStyle: { color: '#93a6bf', fontSize: 11 }
    },
    grid: [
      { left: 10, right: 14, top: 26, height: '60%', containLabel: true },
      { left: 10, right: 14, top: '73%', height: '16%', containLabel: true }
    ],
    xAxis: [
      {
        type: 'category',
        data: categories,
        boundaryGap: true,
        axisLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.16)' } },
        axisTick: { show: false },
        axisLabel: { show: false },
        splitLine: { show: false }
      },
      {
        type: 'category',
        gridIndex: 1,
        data: categories,
        boundaryGap: true,
        axisLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.16)' } },
        axisTick: { show: false },
        axisLabel: { ...AXIS_LABEL, hideOverlap: true },
        splitLine: { show: false }
      }
    ],
    yAxis: [
      {
        type: 'value',
        scale: true,
        position: 'left',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: {
          ...AXIS_LABEL,
          formatter: (value) => fmtPrice(value)
        },
        splitLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.08)' } }
      },
      {
        type: 'value',
        gridIndex: 1,
        scale: true,
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: {
          ...AXIS_LABEL,
          formatter: (value) => fmtCompact(value, 0)
        },
        splitLine: { show: false }
      }
    ],
    dataZoom: [
      {
        type: 'inside',
        xAxisIndex: [0, 1],
        start: zoomStart,
        end: 100,
        zoomOnMouseWheel: true,
        moveOnMouseMove: true
      }
    ],
    series
  }
}

function render() {
  if (!chartEl.value || chartEl.value.clientWidth === 0) return
  if (!chart.value) {
    chart.value = echarts.init(chartEl.value, null, { renderer: 'canvas' })
    chart.value.on('dataZoom', handleDataZoom)
  }
  applyingZoom = true
  chart.value.setOption(buildOption(), true)
  window.requestAnimationFrame(() => {
    applyingZoom = false
  })
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
    colorHandler = handleColorChange
    window.addEventListener('qts:kline-colors', colorHandler)
    render()
    resizeHandler = handleResize
    window.addEventListener('resize', resizeHandler)
  })
})

onBeforeUnmount(() => {
  if (resizeHandler) window.removeEventListener('resize', resizeHandler)
  if (colorHandler) window.removeEventListener('qts:kline-colors', colorHandler)
  if (chart.value) {
    chart.value.off('dataZoom', handleDataZoom)
    chart.value.dispose()
    chart.value = null
  }
})

watch(
  () => props.candles,
  () => render(),
  { deep: true }
)

watch(
  () => props.visibleBars,
  (value) => {
    if (value > 0) {
      visibleCount.value = value
      render()
    }
  }
)
</script>
