<template>
  <header class="app-topbar">
    <button type="button" class="topbar-menu-btn" title="菜单" @click="$emit('open-menu')">
      <el-icon><Fold /></el-icon>
    </button>

    <div class="topbar-page">
      <h1 class="topbar-title">{{ pageTitle }}</h1>
      <span class="topbar-tagline">{{ tagline }}</span>
    </div>

    <div class="topbar-spacer"></div>

    <div class="status-pill" :title="statusTooltip" @click="checkHealth">
      <span class="status-dot" :class="{ 'is-off': healthStatus !== 'ok' }"></span>
      <span>{{ statusLabel }}</span>
    </div>

    <div class="topbar-divider"></div>

    <el-dropdown trigger="click" @command="handleCommand">
      <div class="topbar-user">
        <div class="user-avatar">{{ auth.avatarText }}</div>
        <span class="user-name">{{ auth.displayName }}</span>
        <el-icon class="topbar-arrow"><ArrowDown /></el-icon>
      </div>
      <template #dropdown>
        <el-dropdown-menu>
          <el-dropdown-item command="profile">
            <el-icon><User /></el-icon>
            个人中心
          </el-dropdown-item>
          <el-dropdown-item command="switch">
            <el-icon><Switch /></el-icon>
            切换账号
          </el-dropdown-item>
          <el-dropdown-item command="password" divided>
            <el-icon><Lock /></el-icon>
            修改密码
          </el-dropdown-item>
          <el-dropdown-item command="logout">
            <el-icon><SwitchButton /></el-icon>
            退出登录
          </el-dropdown-item>
        </el-dropdown-menu>
      </template>
    </el-dropdown>

    <el-dialog v-model="switchVisible" title="切换账号" width="440px">
      <div class="account-switch-list">
        <button
          v-for="account in auth.accounts"
          :key="account.user.id"
          type="button"
          class="account-switch-item"
          :class="{ current: String(account.user.id) === String(auth.user?.id) }"
          :disabled="switchingId !== null"
          @click="switchTo(account)"
        >
          <span class="account-switch-avatar">{{ avatarOf(account.user) }}</span>
          <span class="account-switch-meta">
            <b>{{ account.user.displayName || account.user.username }}</b>
            <small>@{{ account.user.username }} · {{ account.user.role === 'admin' ? '管理员' : '交易员' }}</small>
          </span>
          <el-tag v-if="String(account.user.id) === String(auth.user?.id)" size="small" type="success">当前</el-tag>
          <el-icon v-else-if="switchingId === account.user.id" class="is-loading"><Loading /></el-icon>
        </button>
      </div>
      <el-empty v-if="!auth.accounts.length" description="暂无已保存账号" :image-size="72" />
      <template #footer>
        <el-button @click="switchVisible = false">关闭</el-button>
        <el-button type="primary" @click="addAccount">添加账号</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="passwordVisible"
      title="修改密码"
      width="420px"
      :close-on-click-modal="false"
      @closed="resetPasswordForm"
    >
      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="首次登录建议修改初始密码；修改成功后需要重新登录。"
        style="margin-bottom: 16px"
      />
      <el-form ref="passwordFormRef" :model="passwordForm" :rules="passwordRules" label-position="top">
        <el-form-item label="当前密码" prop="oldPassword">
          <el-input v-model="passwordForm.oldPassword" type="password" show-password placeholder="请输入当前密码" />
        </el-form-item>
        <el-form-item label="新密码" prop="newPassword">
          <el-input v-model="passwordForm.newPassword" type="password" show-password placeholder="至少 6 位字符" />
        </el-form-item>
        <el-form-item label="确认新密码" prop="confirmPassword">
          <el-input v-model="passwordForm.confirmPassword" type="password" show-password placeholder="请再次输入新密码" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="passwordSubmitting" @click="passwordVisible = false">取消</el-button>
        <el-button type="primary" :loading="passwordSubmitting" @click="submitPassword">
          保存并重新登录
        </el-button>
      </template>
    </el-dialog>
  </header>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { healthApi } from '@/api'

defineEmits(['open-menu'])

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const pageTitle = computed(() => route.meta?.title || '仪表盘')
const tagline = computed(() => route.meta?.tagline || 'OKX 策略回测与自动交易终端')

/* ---------------- 服务状态 ---------------- */
const healthStatus = ref('checking')
const lastCheckedAt = ref(null)
const statusLabel = computed(() => {
  if (healthStatus.value === 'ok') return '服务正常'
  if (healthStatus.value === 'checking') return '检测中…'
  return '服务离线'
})
const statusTooltip = computed(() => {
  const time = lastCheckedAt.value ? lastCheckedAt.value.toLocaleTimeString('zh-CN', { hour12: false }) : '—'
  return `后端 API 状态 · 最后检测 ${time} · 点击重新检测`
})

async function checkHealth() {
  try {
    const data = await healthApi.check()
    healthStatus.value = data?.status === 'ok' ? 'ok' : 'offline'
  } catch {
    healthStatus.value = 'offline'
  } finally {
    lastCheckedAt.value = new Date()
  }
}

let healthTimer = null
onMounted(() => {
  checkHealth()
  healthTimer = window.setInterval(() => {
    if (document.visibilityState === 'visible') checkHealth()
  }, 30000)
})
onBeforeUnmount(() => {
  if (healthTimer) window.clearInterval(healthTimer)
})

/* ---------------- 用户菜单 ---------------- */
function handleCommand(command) {
  if (command === 'profile') {
    router.push('/profile')
  } else if (command === 'switch') {
    switchVisible.value = true
  } else if (command === 'password') {
    passwordVisible.value = true
  } else if (command === 'logout') {
    logout()
  }
}

/* ---------------- 切换账号 ---------------- */
const switchVisible = ref(false)
const switchingId = ref(null)

function avatarOf(user) {
  const name = user?.displayName || user?.username || 'Q'
  return String(name).trim().slice(0, 1).toUpperCase()
}

async function switchTo(account) {
  if (String(account.user.id) === String(auth.user?.id)) {
    switchVisible.value = false
    return
  }
  switchingId.value = account.user.id
  try {
    await auth.switchAccount(account.user.id)
    switchVisible.value = false
    ElMessage.success(`已切换到 ${auth.displayName}`)
    window.location.href = '/'
  } catch (error) {
    ElMessage.error(error?.message || '切换失败，请重新登录该账号')
  } finally {
    switchingId.value = null
  }
}

function addAccount() {
  switchVisible.value = false
  router.push({ path: '/login', query: { add: '1' } })
}

async function logout() {
  try {
    await ElMessageBox.confirm('确定要退出当前账号吗？', '退出登录', {
      confirmButtonText: '退出登录',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }
  auth.logout()
  ElMessage.success('已退出登录')
  router.replace({ path: '/login' })
}

/* ---------------- 修改密码 ---------------- */
const passwordVisible = ref(false)
const passwordSubmitting = ref(false)
const passwordFormRef = ref(null)
const passwordForm = reactive({
  oldPassword: '',
  newPassword: '',
  confirmPassword: ''
})

const passwordRules = {
  oldPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 6, max: 64, message: '新密码长度需为 6-64 位', trigger: 'blur' }
  ],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: 'blur' },
    {
      validator: (rule, value, callback) => {
        if (value !== passwordForm.newPassword) {
          callback(new Error('两次输入的新密码不一致'))
        } else {
          callback()
        }
      },
      trigger: 'blur'
    }
  ]
}

function resetPasswordForm() {
  passwordForm.oldPassword = ''
  passwordForm.newPassword = ''
  passwordForm.confirmPassword = ''
  passwordFormRef.value?.clearValidate()
}

async function submitPassword() {
  if (!passwordFormRef.value) return
  try {
    await passwordFormRef.value.validate()
  } catch {
    return
  }
  passwordSubmitting.value = true
  try {
    await auth.changePassword(passwordForm.oldPassword, passwordForm.newPassword)
    passwordVisible.value = false
    auth.logout()
    ElMessage.success('密码已修改，请使用新密码重新登录')
    router.replace({ path: '/login' })
  } catch (error) {
    ElMessage.error(error?.message || '修改密码失败')
  } finally {
    passwordSubmitting.value = false
  }
}
</script>

<style scoped>
.account-switch-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.account-switch-item {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--qts-border, #263247);
  border-radius: 10px;
  background: var(--qts-card, #111a2c);
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition: border-color 0.16s ease, background 0.16s ease;
}

.account-switch-item:hover {
  border-color: var(--qts-primary, #22d3ee);
}

.account-switch-item.current {
  border-color: rgba(34, 211, 238, 0.55);
  background: rgba(34, 211, 238, 0.08);
}

.account-switch-item:disabled {
  cursor: wait;
  opacity: 0.7;
}

.account-switch-avatar {
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: linear-gradient(135deg, #22d3ee, #3b82f6);
  color: #06121f;
  font-weight: 700;
  flex: none;
}

.account-switch-meta {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
}

.account-switch-meta small {
  color: var(--qts-text-dim, #8090a7);
}
</style>
