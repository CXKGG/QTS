import axios from 'axios'

/**
 * QTS 统一 HTTP 客户端
 * - baseURL: /api（开发环境由 Vite 代理到 http://localhost:5088）
 * - 自动注入 Bearer Token
 * - 统一拆解 { success, data, message, code } 信封
 * - 401 时清理本地会话并跳转登录页
 */

const TOKEN_KEY = 'qts_token'
const USER_KEY = 'qts_user'

export class ApiError extends Error {
  constructor(message, options = {}) {
    super(message || '请求失败')
    this.name = 'ApiError'
    this.status = options.status ?? 0
    this.code = options.code ?? null
    this.business = Boolean(options.business)
  }
}

let unauthorizedHandler = null
export function setUnauthorizedHandler(handler) {
  unauthorizedHandler = handler
}

export function getStoredToken() {
  try {
    return localStorage.getItem(TOKEN_KEY) || ''
  } catch {
    return ''
  }
}

export function clearStoredSession() {
  try {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
  } catch {
    /* ignore */
  }
}

const http = axios.create({
  baseURL: '/api',
  timeout: 120000,
  headers: { Accept: 'application/json' }
})

http.interceptors.request.use((config) => {
  const token = getStoredToken()
  if (token) {
    if (config.headers && typeof config.headers.set === 'function') {
      config.headers.set('Authorization', `Bearer ${token}`)
    } else {
      config.headers = { ...(config.headers || {}), Authorization: `Bearer ${token}` }
    }
  }
  return config
})

function readMessage(body) {
  if (!body) return ''
  if (typeof body === 'string') {
    const text = body.trim()
    if (!text || text.startsWith('<')) return ''
    return text
  }
  if (typeof body === 'object') {
    if (typeof body.message === 'string' && body.message.trim()) return body.message
    if (typeof body.title === 'string' && body.title.trim()) return body.title
    if (body.errors && typeof body.errors === 'object') {
      const values = Object.values(body.errors).flat()
      if (values.length && typeof values[0] === 'string') return values[0]
    }
  }
  return ''
}

function notifyUnauthorized() {
  try {
    if (typeof unauthorizedHandler === 'function') unauthorizedHandler()
  } catch {
    /* ignore */
  }
}

http.interceptors.response.use(
  (response) => {
    const body = response.data
    if (body && typeof body === 'object' && !Array.isArray(body) && Object.prototype.hasOwnProperty.call(body, 'success')) {
      if (body.success) return body.data
      throw new ApiError(body.message || '请求失败', {
        status: response.status,
        code: body.code ?? null,
        business: true
      })
    }
    return body
  },
  (error) => {
    if (error instanceof ApiError) return Promise.reject(error)
    if (axios.isCancel && axios.isCancel(error)) {
      return Promise.reject(new ApiError('请求已取消', { status: 0 }))
    }
    const status = error.response?.status ?? 0
    const body = error.response?.data
    let message = readMessage(body)
    if (!message) {
      if (status === 401) message = '登录状态已失效，请重新登录'
      else if (status === 403) message = '没有权限执行该操作'
      else if (status === 404) message = '请求的资源不存在'
      else if (status === 409) message = '数据状态冲突，请刷新后重试'
      else if (status >= 500) message = '服务器开小差了，请稍后重试'
      else if (error.code === 'ECONNABORTED') message = '请求超时，请稍后重试'
      else if (error.code === 'ERR_NETWORK' || !error.response) message = '无法连接后端服务，请确认服务已启动'
      else message = `请求失败（${status}）`
    }
    if (status === 401) {
      clearStoredSession()
      notifyUnauthorized()
    }
    return Promise.reject(
      new ApiError(message, { status, code: body?.code ?? null })
    )
  }
)

/** 分页数据归一化：兼容 { items, total, page, pageSize } 与裸数组两种形态 */
export function normalizePaged(payload, fallbackPage = 1, fallbackPageSize = 20) {
  if (Array.isArray(payload)) {
    return {
      items: payload,
      total: payload.length,
      page: fallbackPage,
      pageSize: payload.length || fallbackPageSize
    }
  }
  const items = Array.isArray(payload?.items) ? payload.items : []
  return {
    items,
    total: Number(payload?.total ?? items.length) || 0,
    page: Number(payload?.page ?? fallbackPage) || fallbackPage,
    pageSize: Number(payload?.pageSize ?? (items.length || fallbackPageSize)) || fallbackPageSize
  }
}

/** 去掉空查询参数，避免后端收到 "" 造成筛选异常 */
export function cleanParams(params = {}) {
  const result = {}
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue
    result[key] = value
  }
  return result
}

export { http }

/* ------------------------------------------------------------------ */
/* Auth                                                                */
/* ------------------------------------------------------------------ */
export const authApi = {
  login: (payload) => http.post('/auth/login', payload),
  register: (payload) => http.post('/auth/register', payload),
  me: () => http.get('/auth/me'),
  updateProfile: (payload) => http.put('/auth/profile', payload),
  changePassword: (payload) => http.post('/auth/change-password', payload)
}

/* ------------------------------------------------------------------ */
/* Dashboard                                                           */
/* ------------------------------------------------------------------ */
export const dashboardApi = {
  overview: () => http.get('/dashboard/overview'),
  equityCurve: (range = '30d') => http.get('/dashboard/equity-curve', { params: { range } })
}

/* ------------------------------------------------------------------ */
/* Market                                                              */
/* ------------------------------------------------------------------ */
export const marketApi = {
  instruments: () => http.get('/market/instruments'),
  candles: ({ instId, bar = '1H', limit = 300 }) =>
    http.get('/market/candles', { params: cleanParams({ instId, bar, limit }) }),
  ticker: (instId) => http.get('/market/ticker', { params: { instId } }),
  sync: (payload) => http.post('/market/sync', payload),
  syncAll: (payload) => http.post('/market/sync-all', payload)
}

/* ------------------------------------------------------------------ */
/* Strategies                                                          */
/* ------------------------------------------------------------------ */
export const strategyApi = {
  list: () => http.get('/strategies')
}

/* ------------------------------------------------------------------ */
/* AI decision                                                         */
/* ------------------------------------------------------------------ */
export const aiApi = {
  status: () => http.get('/ai/status'),
  analyze: (payload) => http.post('/ai/analyze', payload)
}

/* ------------------------------------------------------------------ */
/* Backtests                                                           */
/* ------------------------------------------------------------------ */
export const backtestApi = {
  create: (payload) => http.post('/backtests', payload),
  list: (params = {}) => http.get('/backtests', { params: cleanParams(params) }),
  detail: (id) => http.get(`/backtests/${id}`),
  remove: (id) => http.delete(`/backtests/${id}`)
}

/* ------------------------------------------------------------------ */
/* Trading tasks                                                       */
/* ------------------------------------------------------------------ */
export const taskApi = {
  list: (params = {}) => http.get('/tasks', { params: cleanParams(params) }),
  detail: (id) => http.get(`/tasks/${id}`),
  create: (payload) => http.post('/tasks', payload),
  update: (id, payload) => http.put(`/tasks/${id}`, payload),
  start: (id) => http.post(`/tasks/${id}/start`),
  stop: (id) => http.post(`/tasks/${id}/stop`),
  closePosition: (id) => http.post(`/tasks/${id}/close-position`),
  remove: (id) => http.delete(`/tasks/${id}`)
}

/* ------------------------------------------------------------------ */
/* Trade desk (manual simulated orders)                                */
/* ------------------------------------------------------------------ */
export const tradeApi = {
  overview: (params = {}) => http.get('/trade/overview', { params: cleanParams(params) }),
  open: (payload) => http.post('/trade/open', payload),
  close: (payload) => http.post('/trade/close', payload)
}

/* ------------------------------------------------------------------ */
/* Orders & positions                                                  */
/* ------------------------------------------------------------------ */
export const orderApi = {
  list: (params = {}) => http.get('/orders', { params: cleanParams(params) }),
  positions: (params = {}) => http.get('/positions', { params: cleanParams(params) })
}

/* ------------------------------------------------------------------ */
/* Settings & system                                                   */
/* ------------------------------------------------------------------ */
export const settingsApi = {
  getOkx: () => http.get('/settings/okx'),
  saveOkx: (payload) => http.put('/settings/okx', payload),
  testOkx: () => http.post('/settings/okx/test'),
  getAi: () => http.get('/settings/ai'),
  saveAi: (payload) => http.put('/settings/ai', payload),
  resetPaperAccount: (balance) => http.post('/settings/paper-account/reset', { balance }),
  system: () => http.get('/settings/system')
}

/* ------------------------------------------------------------------ */
/* Health                                                              */
/* ------------------------------------------------------------------ */
export const healthApi = {
  check: () => http.get('/health', { timeout: 8000 })
}
