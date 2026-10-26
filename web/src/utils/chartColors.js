const STORAGE_KEY = 'qts_kline_colors_v2'

export const DEFAULT_KLINE_COLORS = Object.freeze({
  up: '#34d399',
  down: '#fb7185'
})

export const KLINE_COLOR_PRESETS = Object.freeze([
  {
    key: 'green-up',
    label: '绿涨红跌',
    hint: '国际行情习惯',
    up: '#34d399',
    down: '#fb7185'
  },
  {
    key: 'red-up',
    label: '红涨绿跌',
    hint: 'A 股 / 国内习惯',
    up: '#fb7185',
    down: '#34d399'
  }
])

const HEX_RE = /^#[0-9a-fA-F]{6}$/

function normalizeColor(value, fallback) {
  return typeof value === 'string' && HEX_RE.test(value) ? value.toLowerCase() : fallback
}

export function getKlineColors() {
  if (typeof window === 'undefined') return { ...DEFAULT_KLINE_COLORS }
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (!raw) return { ...DEFAULT_KLINE_COLORS }
    const parsed = JSON.parse(raw)
    return {
      up: normalizeColor(parsed?.up, DEFAULT_KLINE_COLORS.up),
      down: normalizeColor(parsed?.down, DEFAULT_KLINE_COLORS.down)
    }
  } catch {
    return { ...DEFAULT_KLINE_COLORS }
  }
}

export function saveKlineColors(colors) {
  const next = {
    up: normalizeColor(colors?.up, DEFAULT_KLINE_COLORS.up),
    down: normalizeColor(colors?.down, DEFAULT_KLINE_COLORS.down)
  }
  if (typeof window !== 'undefined') {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
    } catch {
      /* localStorage may be disabled; still update in-memory listeners */
    }
    window.dispatchEvent(new CustomEvent('qts:kline-colors', { detail: next }))
  }
  return next
}

export function matchKlinePreset(colors) {
  const matched = KLINE_COLOR_PRESETS.find(
    (preset) => preset.up === colors?.up && preset.down === colors?.down
  )
  return matched?.key || 'custom'
}

export function hexToRgba(hex, alpha = 1) {
  const normalized = normalizeColor(hex, DEFAULT_KLINE_COLORS.up).slice(1)
  const r = Number.parseInt(normalized.slice(0, 2), 16)
  const g = Number.parseInt(normalized.slice(2, 4), 16)
  const b = Number.parseInt(normalized.slice(4, 6), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}
