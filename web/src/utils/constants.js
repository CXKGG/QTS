/* 全局共享常量与枚举映射 */

export const BAR_OPTIONS = [
  { label: '1分钟', value: '1m' },
  { label: '5分钟', value: '5m' },
  { label: '15分钟', value: '15m' },
  { label: '1小时', value: '1H' },
  { label: '4小时', value: '4H' },
  { label: '1天', value: '1D' }
]

export const BAR_LABELS = BAR_OPTIONS.reduce((acc, item) => {
  acc[item.value] = item.label
  return acc
}, {})

export const RANGE_TABS = [
  { label: '7天', value: '7d' },
  { label: '30天', value: '30d' },
  { label: '90天', value: '90d' },
  { label: '全部', value: 'all' }
]

export const DEFAULT_INSTRUMENTS = [
  { instId: 'BTC-USDT', base: 'BTC', quote: 'USDT', name: 'BTC/USDT' },
  { instId: 'ETH-USDT', base: 'ETH', quote: 'USDT', name: 'ETH/USDT' },
  { instId: 'SOL-USDT', base: 'SOL', quote: 'USDT', name: 'SOL/USDT' },
  { instId: 'DOGE-USDT', base: 'DOGE', quote: 'USDT', name: 'DOGE/USDT' },
  { instId: 'XRP-USDT', base: 'XRP', quote: 'USDT', name: 'XRP/USDT' }
]

export const TASK_STATUS_OPTIONS = [
  { label: '全部状态', value: '' },
  { label: '运行中', value: 'Running' },
  { label: '已停止', value: 'Stopped' },
  { label: '已暂停', value: 'Paused' },
  { label: '异常', value: 'Error' }
]

export const TASK_STATUS_META = {
  Running: { label: '运行中', type: 'success' },
  Stopped: { label: '已停止', type: 'info' },
  Paused: { label: '已暂停', type: 'warning' },
  Error: { label: '异常', type: 'danger' }
}

export const MODE_LABELS = {
  Paper: '模拟盘',
  Live: '实盘'
}

export const ORDER_STATUS_OPTIONS = [
  { label: '全部状态', value: '' },
  { label: '待成交', value: 'Pending' },
  { label: '已成交', value: 'Filled' },
  { label: '已撤销', value: 'Canceled' },
  { label: '失败', value: 'Failed' }
]

export const ORDER_STATUS_META = {
  Filled: { label: '已成交', type: 'success' },
  Pending: { label: '待成交', type: 'warning' },
  PartiallyFilled: { label: '部分成交', type: 'warning' },
  Canceled: { label: '已撤销', type: 'info' },
  Failed: { label: '失败', type: 'danger' },
  Rejected: { label: '已拒绝', type: 'danger' }
}

export const ORDER_TYPE_LABELS = {
  Market: '市价',
  Limit: '限价'
}

export const SIGNAL_META = {
  LONG: { label: 'LONG 多', type: 'success' },
  SHORT: { label: 'SHORT 空', type: 'danger' },
  FLAT: { label: 'FLAT 空仓', type: 'info' }
}

export const SIDE_OPTIONS = [
  { label: '做多', value: 'long' },
  { label: '做空', value: 'short' }
]

export const SIDE_META = {
  long: { label: '多', type: 'success' },
  short: { label: '空', type: 'danger' }
}

export const CATEGORY_PALETTE = ['#22d3ee', '#a78bfa', '#34d399', '#fbbf24', '#fb7185', '#60a5fa', '#f472b6', '#2dd4bf']

export function categoryColor(category) {
  const text = String(category || '')
  let hash = 0
  for (let i = 0; i < text.length; i += 1) {
    hash = (hash * 31 + text.charCodeAt(i)) % 100000
  }
  return CATEGORY_PALETTE[hash % CATEGORY_PALETTE.length]
}

export function categoryIcon(category) {
  const text = String(category || '')
  if (text.includes('趋势')) return 'TrendCharts'
  if (text.includes('套利')) return 'Switch'
  if (text.includes('网格')) return 'Grid'
  if (text.includes('震荡') || text.includes('回归')) return 'DataLine'
  if (text.includes('突破')) return 'Aim'
  if (text.includes('量')) return 'Histogram'
  return 'Cpu'
}

export function barLabel(bar) {
  return BAR_LABELS[bar] || bar || '—'
}

export function modeLabel(mode) {
  return MODE_LABELS[mode] || mode || '—'
}

export function taskStatusMeta(status) {
  return TASK_STATUS_META[status] || { label: status || '未知', type: 'info' }
}

export function sideMeta(side) {
  const key = String(side || '').toLowerCase()
  return SIDE_META[key] || { label: side || '—', type: 'info' }
}

export function orderStatusMeta(status) {
  return ORDER_STATUS_META[status] || { label: status || '未知', type: 'info' }
}

export function signalMeta(signal) {
  return SIGNAL_META[String(signal || '').toUpperCase()] || { label: signal || '—', type: 'info' }
}

export function orderTypeLabel(type) {
  return ORDER_TYPE_LABELS[type] || type || '—'
}
