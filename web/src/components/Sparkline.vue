<template>
  <span class="sparkline-wrap">
    <svg
      :viewBox="`0 0 ${width} ${height}`"
      preserveAspectRatio="none"
      class="sparkline"
      aria-hidden="true"
    >
      <defs>
        <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" :stop-color="color" stop-opacity="0.32" />
          <stop offset="100%" :stop-color="color" stop-opacity="0" />
        </linearGradient>
      </defs>
      <path v-if="areaPath" :d="areaPath" :fill="`url(#${gradientId})`" />
      <polyline
        v-if="linePoints"
        :points="linePoints"
        fill="none"
        :stroke="color"
        stroke-width="1.6"
        stroke-linejoin="round"
        stroke-linecap="round"
      />
    </svg>
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  values: { type: Array, default: () => [] },
  color: { type: String, default: '#22d3ee' },
  width: { type: Number, default: 100 },
  height: { type: Number, default: 28 }
})

const gradientId = `qts-spark-${Math.random().toString(36).slice(2, 9)}`

const numericValues = computed(() =>
  (props.values || []).map((item) => Number(item)).filter((item) => Number.isFinite(item))
)

const normalized = computed(() => {
  const values = numericValues.value
  if (values.length < 2) return []
  const min = Math.min(...values)
  const max = Math.max(...values)
  const span = max - min || 1
  const pad = 2
  const usableHeight = props.height - pad * 2
  return values.map((value, index) => {
    const x = (index / (values.length - 1)) * props.width
    const y = pad + (1 - (value - min) / span) * usableHeight
    return [Number(x.toFixed(2)), Number(y.toFixed(2))]
  })
})

const linePoints = computed(() =>
  normalized.value.length ? normalized.value.map((point) => point.join(',')).join(' ') : ''
)

const areaPath = computed(() => {
  const points = normalized.value
  if (!points.length) return ''
  const [first] = points
  const last = points[points.length - 1]
  return `M ${first[0]},${props.height} L ${points.map((point) => point.join(',')).join(' L ')} L ${last[0]},${props.height} Z`
})
</script>

<style scoped>
.sparkline-wrap {
  display: inline-block;
  width: 100%;
  height: 100%;
}

.sparkline {
  display: block;
  width: 100%;
  height: 100%;
  overflow: visible;
}
</style>
