import { defineStore } from 'pinia'
import { authApi } from '@/api'

const TOKEN_KEY = 'qts_token'
const USER_KEY = 'qts_user'
const ACCOUNTS_KEY = 'qts_accounts_v1'
const MAX_ACCOUNTS = 8

function readStoredUser() {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

function readAccounts() {
  try {
    const raw = localStorage.getItem(ACCOUNTS_KEY)
    const list = raw ? JSON.parse(raw) : []
    return Array.isArray(list) ? list.filter((item) => item?.token && item?.user?.id) : []
  } catch {
    return []
  }
}

function writeAccounts(accounts) {
  try {
    localStorage.setItem(ACCOUNTS_KEY, JSON.stringify(accounts.slice(0, MAX_ACCOUNTS)))
  } catch {
    /* ignore */
  }
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: localStorage.getItem(TOKEN_KEY) || '',
    user: readStoredUser(),
    accounts: readAccounts(),
    pending: false
  }),
  getters: {
    isAuthenticated: (state) => Boolean(state.token),
    displayName: (state) => state.user?.displayName || state.user?.username || '交易员',
    username: (state) => state.user?.username || '',
    avatarText: (state) => {
      const name = state.user?.displayName || state.user?.username || 'Q'
      return String(name).trim().slice(0, 1).toUpperCase()
    },
    otherAccounts: (state) =>
      state.accounts.filter((item) => String(item.user?.id) !== String(state.user?.id))
  },
  actions: {
    rememberAccount(data) {
      if (!data?.token || !data?.user?.id) return
      const record = {
        token: data.token,
        user: data.user,
        savedAt: new Date().toISOString()
      }
      const rest = this.accounts.filter((item) => String(item.user?.id) !== String(data.user.id))
      this.accounts = [record, ...rest].slice(0, MAX_ACCOUNTS)
      writeAccounts(this.accounts)
    },
    applySession(data) {
      this.token = data?.token || ''
      this.user = data?.user || null
      if (this.token) localStorage.setItem(TOKEN_KEY, this.token)
      if (this.user) localStorage.setItem(USER_KEY, JSON.stringify(this.user))
    },
    async login(payload) {
      this.pending = true
      try {
        const data = await authApi.login(payload)
        this.applySession(data)
        this.rememberAccount(data)
        return data
      } finally {
        this.pending = false
      }
    },
    async register(payload) {
      this.pending = true
      try {
        const data = await authApi.register(payload)
        this.applySession(data)
        this.rememberAccount(data)
        return data
      } finally {
        this.pending = false
      }
    },
    async fetchMe() {
      const me = await authApi.me()
      if (me) {
        this.user = me
        localStorage.setItem(USER_KEY, JSON.stringify(me))
        const current = this.accounts.find((item) => String(item.user?.id) === String(me.id))
        if (current) {
          current.user = me
          writeAccounts(this.accounts)
        }
      }
      return me
    },
    async updateProfile(payload) {
      const user = await authApi.updateProfile(payload)
      if (user) {
        this.user = user
        localStorage.setItem(USER_KEY, JSON.stringify(user))
        const current = this.accounts.find((item) => String(item.user?.id) === String(user.id))
        if (current) {
          current.user = user
          writeAccounts(this.accounts)
        }
      }
      return user
    },
    async switchAccount(accountId) {
      const record = this.accounts.find((item) => String(item.user?.id) === String(accountId))
      if (!record) throw new Error('账号不存在，请重新登录')
      this.pending = true
      try {
        this.applySession(record)
        const me = await authApi.me()
        this.user = me
        localStorage.setItem(USER_KEY, JSON.stringify(me))
        record.user = me
        writeAccounts(this.accounts)
        return me
      } catch (error) {
        this.accounts = this.accounts.filter((item) => String(item.user?.id) !== String(accountId))
        writeAccounts(this.accounts)
        this.logout()
        throw error
      } finally {
        this.pending = false
      }
    },
    removeAccount(accountId) {
      const isCurrent = String(this.user?.id) === String(accountId)
      this.accounts = this.accounts.filter((item) => String(item.user?.id) !== String(accountId))
      writeAccounts(this.accounts)
      if (isCurrent) this.logout()
    },
    async changePassword(oldPassword, newPassword) {
      return authApi.changePassword({ oldPassword, newPassword })
    },
    logout() {
      this.token = ''
      this.user = null
      try {
        localStorage.removeItem(TOKEN_KEY)
        localStorage.removeItem(USER_KEY)
      } catch {
        /* ignore */
      }
    }
  }
})
