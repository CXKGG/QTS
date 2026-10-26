/**
 * 通用格式化工具
 * 数字统一使用固定小数位 + 千分位，并配合 tabular-nums 等宽数字字体。
 */

const EMPTY = '—'

export function isNil(value) {
  return value === null || value === undefined || value === ''
}

export function toNumber(value) {
  if (isNil(value)) return null
  const num = Number(value)
  return Number.isFinite(num) ? num : null
}

/** 金额/数量：固定小数位 + 千分位 */
export function fmtMoney(value, digits = 2) {
  const num = toNumber(value)
  if (num === null) return EMPTY
  return num.toLocaleString('en-US', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits
  })
}

/** 数字（不强制补零到很多位，仍保留千分位） */
export function fmtNumber(value, digits = 2) {
  return fmtMoney(value, digits)
}

/** 币价：按量级自动选择小数位 */
export function fmtPrice(value) {
  const num = toNumber(value)
  if (num === null) return EMPTY
  const abs = Math.abs(num)
  let digits = 2
  if (abs > 0 && abs < 0.01) digits = 6
  else if (abs < 1) digits = 4
  else if (abs < 100) digits = 3
  return num.toLocaleString('en-US', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits
  })
}

/** 百分比：默认带正负号 */
export function fmtPct(value, digits = 2, signed = true) {
  const num = toNumber(value)
  if (num === null) return EMPTY
  const prefix = signed && num > 0 ? '+' : ''
  return `${prefix}${num.toFixed(digits)}%`
}

/** 紧凑数字：成交量等大数展示 */
export function fmtCompact(value, digits = 2) {
  const num = toNumber(value)
  if (num === null) return EMPTY
  const abs = Math.abs(num)
  if (abs >= 1e12) return `${(num / 1e12).toFixed(digits)} 万亿`
  if (abs >= 1e8) return `${(num / 1e8).toFixed(digits)} 亿`
  if (abs >= 1e4) return `${(num / 1e4).toFixed(digits)} 万`
  return num.toLocaleString('en-US', { maximumFractionDigits: digits })
}

function pad(value, length = 2) {
  return String(value).padStart(length, '0')
}

export function toDate(value) {
  if (isNil(value)) return null
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value
  if (typeof value === 'number') {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? null : date
  }
  const text = String(value).trim()
  if (!text) return null
  if (/^\d{10,13}$/.test(text)) {
    const num = Number(text)
    const date = new Date(text.length === 10 ? num * 1000 : num)
    return Number.isNaN(date.getTime()) ? null : date
  }
  const direct = new Date(text)
  if (!Number.isNaN(direct.getTime())) return direct
  const fallback = new Date(text.replace(' ', 'T'))
  return Number.isNaN(fallback.getTime()) ? null : fallback
}

/** YYYY-MM-DD HH:mm:ss */
export function fmtDateTime(value) {
  const date = toDate(value)
  if (!date) return EMPTY
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

/** YYYY-MM-DD */
export function fmtDate(value) {
  const date = toDate(value)
  if (!date) return EMPTY
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** HH:mm */
export function fmtClock(value) {
  const date = toDate(value)
  if (!date) return EMPTY
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** 毫秒时间戳 → 图表轴标签 */
export function fmtChartTime(value) {
  const date = toDate(value)
  if (!date) return ''
  return `${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** 毫秒 → 人类可读耗时 */
export function fmtDuration(ms) {
  const num = toNumber(ms)
  if (num === null || num < 0) return EMPTY
  if (num < 1000) return `${Math.max(1, Math.round(num))} 毫秒`
  if (num < 60000) return `${(num / 1000).toFixed(1)} 秒`
  return `${(num / 60000).toFixed(1)} 分钟`
}

/** 相对时间：刚刚 / x 分钟前 / x 小时前 / 日期 */
export function fmtRelative(value) {
  const date = toDate(value)
  if (!date) return EMPTY
  const diff = Date.now() - date.getTime()
  if (diff < 0) return fmtDateTime(value)
  if (diff < 60 * 1000) return '刚刚'
  if (diff < 60 * 60 * 1000) return `${Math.floor(diff / 60000)} 分钟前`
  if (diff < 24 * 60 * 60 * 1000) return `${Math.floor(diff / 3600000)} 小时前`
  if (diff < 7 * 24 * 60 * 60 * 1000) return `${Math.floor(diff / 86400000)} 天前`
  return fmtDate(value)
}

/** 盈亏色值 class */
export function pnlClass(value) {
  const num = toNumber(value)
  if (num === null || num === 0) return 'is-flat'
  return num > 0 ? 'is-profit' : 'is-loss'
}

/** 是否真正有数值（用于决定是否显示 —） */
export function hasValue(value) {
  return toNumber(value) !== null
}

export { EMPTY }
