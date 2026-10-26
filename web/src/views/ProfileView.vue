<template>
  <div class="page-stack">
    <PageHeader title="个人中心" subtitle="账号资料、安全设置与多账号切换">
      <el-tag v-if="auth.user" size="small" effect="plain" type="info">{{ roleLabel }}</el-tag>
    </PageHeader>

    <div class="profile-grid">
      <section class="qts-card profile-card">
        <div class="profile-cover"></div>
        <div class="profile-identity">
          <div class="profile-avatar">{{ auth.avatarText }}</div>
          <div class="profile-name">
            <h2>{{ auth.displayName }}</h2>
            <p>@{{ auth.username }} · {{ roleLabel }}</p>
          </div>
          <el-tag size="small" :type="auth.user?.role === 'admin' ? 'warning' : 'success'" effect="plain">
            {{ auth.user?.role === 'admin' ? '管理员' : '普通用户' }}
          </el-tag>
        </div>
        <div class="profile-meta">
          <div class="meta-item">
            <span>邮箱</span>
            <b>{{ auth.user?.email || '未设置' }}</b>
          </div>
          <div class="meta-item">
            <span>注册时间</span>
            <b>{{ formatDateTime(auth.user?.createdAt) }}</b>
          </div>
          <div class="meta-item">
            <span>最后登录</span>
            <b>{{ formatDateTime(auth.user?.lastLoginAt) }}</b>
          </div>
          <div class="meta-item">
            <span>会话状态</span>
            <b class="online"><i></i> 已登录</b>
          </div>
        </div>
      </section>

      <section class="qts-card">
        <div class="card-head">
          <div class="card-title">编辑个人资料</div>
        </div>
        <div class="card-body">
          <el-form ref="profileFormRef" :model="profileForm" :rules="profileRules" label-position="top" @submit.prevent="saveProfile">
            <el-form-item label="用户名">
              <el-input :model-value="auth.username" disabled />
            </el-form-item>
            <el-form-item label="昵称" prop="displayName">
              <el-input v-model="profileForm.displayName" maxlength="32" show-word-limit placeholder="界面展示名称" />
            </el-form-item>
            <el-form-item label="邮箱" prop="email">
              <el-input v-model="profileForm.email" placeholder="可选，用于账号识别" />
            </el-form-item>
            <el-button type="primary" native-type="submit" :loading="savingProfile">保存资料</el-button>
          </el-form>
        </div>
      </section>

      <section class="qts-card">
        <div class="card-head">
          <div class="card-title">修改密码</div>
        </div>
        <div class="card-body">
          <el-form ref="passwordFormRef" :model="passwordForm" :rules="passwordRules" label-position="top" @submit.prevent="savePassword">
            <el-form-item label="当前密码" prop="oldPassword">
              <el-input v-model="passwordForm.oldPassword" type="password" show-password autocomplete="current-password" />
            </el-form-item>
            <el-form-item label="新密码" prop="newPassword">
              <el-input v-model="passwordForm.newPassword" type="password" show-password autocomplete="new-password" />
            </el-form-item>
            <el-form-item label="确认新密码" prop="confirmPassword">
              <el-input v-model="passwordForm.confirmPassword" type="password" show-password autocomplete="new-password" />
            </el-form-item>
            <el-button type="primary" native-type="submit" :loading="savingPassword">更新密码</el-button>
          </el-form>
        </div>
      </section>

      <section class="qts-card accounts-card">
        <div class="card-head">
          <div class="card-title">已保存账号</div>
          <div class="card-extra">
            <el-button size="small" @click="addAccount">添加账号</el-button>
          </div>
        </div>
        <div class="card-body">
          <div class="account-list">
            <button
              v-for="account in auth.accounts"
              :key="account.user.id"
              type="button"
              class="account-row"
              :class="{ current: String(account.user.id) === String(auth.user?.id) }"
              :disabled="switchingId !== null"
              @click="switchTo(account)"
            >
              <span class="account-avatar">{{ avatarOf(account.user) }}</span>
              <span class="account-info">
                <b>{{ account.user.displayName || account.user.username }}</b>
                <small>@{{ account.user.username }} · {{ account.user.role === 'admin' ? '管理员' : '交易员' }}</small>
              </span>
              <el-tag v-if="String(account.user.id) === String(auth.user?.id)" size="small" type="success">当前</el-tag>
              <el-icon v-else-if="switchingId === account.user.id" class="is-loading"><Loading /></el-icon>
              <el-button
                v-if="String(account.user.id) !== String(auth.user?.id)"
                size="small"
                text
                type="danger"
                @click.stop="removeAccount(account)"
              >
                移除
              </el-button>
            </button>
          </div>
          <el-empty v-if="!auth.accounts.length" description="暂无已保存账号" :image-size="64" />
          <p class="accounts-hint">
            切换账号使用本地保存的登录会话，不会退出其他账号；移除仅影响本机记录。所有账号共享同一 OKX API 账户与交易数据。
          </p>
        </div>
      </section>

      <section class="qts-card session-card">
        <div class="card-head">
          <div class="card-title">会话与安全</div>
        </div>
        <div class="card-body session-body">
          <div class="session-row">
            <span>当前账号 ID</span>
            <b>#{{ auth.user?.id }}</b>
          </div>
          <div class="session-row">
            <span>登录方式</span>
            <b>Bearer Token（HS256 HMAC）</b>
          </div>
          <div class="session-row">
            <span>Token 存储</span>
            <b>浏览器 localStorage</b>
          </div>
          <div class="session-actions">
            <el-button @click="logout">退出当前账号</el-button>
            <el-button v-if="auth.otherAccounts.length" type="primary" plain @click="switchVisible = true">
              切换其他账号（{{ auth.otherAccounts.length }}）
            </el-button>
          </div>
          <el-alert
            type="warning"
            :closable="false"
            show-icon
            title="安全提示"
            description="请勿在公共电脑保存交易账号。OKX API Key 仅由系统设置统一管理，个人账号不单独保存交易所密钥。"
          />
        </div>
      </section>
    </div>

    <el-dialog v-model="switchVisible" title="切换其他账号" width="420px">
      <div class="account-list dialog-list">
        <button
          v-for="account in auth.otherAccounts"
          :key="account.user.id"
          type="button"
          class="account-row"
          :disabled="switchingId !== null"
          @click="switchTo(account)"
        >
          <span class="account-avatar">{{ avatarOf(account.user) }}</span>
          <span class="account-info">
            <b>{{ account.user.displayName || account.user.username }}</b>
            <small>@{{ account.user.username }}</small>
          </span>
          <el-icon><Right /></el-icon>
        </button>
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import PageHeader from '@/components/PageHeader.vue'

const router = useRouter()
const auth = useAuthStore()

const profileFormRef = ref(null)
const passwordFormRef = ref(null)
const savingProfile = ref(false)
const savingPassword = ref(false)
const switchingId = ref(null)
const switchVisible = ref(false)

const profileForm = reactive({
  displayName: auth.user?.displayName || '',
  email: auth.user?.email || ''
})

const passwordForm = reactive({
  oldPassword: '',
  newPassword: '',
  confirmPassword: ''
})

const profileRules = {
  displayName: [
    { required: true, message: '昵称不能为空', trigger: 'blur' },
    { max: 32, message: '昵称不能超过 32 个字符', trigger: 'blur' }
  ],
  email: [{ type: 'email', message: '请输入有效的邮箱地址', trigger: 'blur' }]
}

const passwordRules = {
  oldPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 6, max: 64, message: '密码长度需为 6-64 位', trigger: 'blur' }
  ],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: 'blur' },
    {
      validator: (_rule, value, callback) => {
        if (value !== passwordForm.newPassword) callback(new Error('两次输入的新密码不一致'))
        else callback()
      },
      trigger: 'blur'
    }
  ]
}

const roleLabel = auth.user?.role === 'admin' ? '管理员' : '交易员'

function formatDateTime(value) {
  if (!value) return '—'
  const text = String(value)
  const hasZone = /Z$|[+-]\d{2}:\d{2}$/.test(text)
  const date = new Date(hasZone ? text : `${text}Z`)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString('zh-CN', { hour12: false })
}

function avatarOf(user) {
  const name = user?.displayName || user?.username || 'Q'
  return String(name).trim().slice(0, 1).toUpperCase()
}

async function saveProfile() {
  if (!profileFormRef.value) return
  try {
    await profileFormRef.value.validate()
  } catch {
    return
  }
  savingProfile.value = true
  try {
    await auth.updateProfile({
      displayName: profileForm.displayName.trim(),
      email: profileForm.email.trim() || null
    })
    ElMessage.success('个人资料已更新')
  } catch (error) {
    ElMessage.error(error?.message || '保存失败')
  } finally {
    savingProfile.value = false
  }
}

async function savePassword() {
  if (!passwordFormRef.value) return
  try {
    await passwordFormRef.value.validate()
  } catch {
    return
  }
  savingPassword.value = true
  try {
    await auth.changePassword(passwordForm.oldPassword, passwordForm.newPassword)
    passwordForm.oldPassword = ''
    passwordForm.newPassword = ''
    passwordForm.confirmPassword = ''
    ElMessage.success('密码已更新，请使用新密码重新登录')
    auth.logout()
    router.replace('/login')
  } catch (error) {
    ElMessage.error(error?.message || '修改密码失败')
  } finally {
    savingPassword.value = false
  }
}

async function switchTo(account) {
  if (String(account.user.id) === String(auth.user?.id)) return
  switchingId.value = account.user.id
  try {
    await auth.switchAccount(account.user.id)
    switchVisible.value = false
    ElMessage.success(`已切换到 ${auth.displayName}`)
    window.location.href = '/'
  } catch (error) {
    ElMessage.error(error?.message || '该账号登录已失效，请重新登录')
  } finally {
    switchingId.value = null
  }
}

function addAccount() {
  router.push({ path: '/login', query: { add: '1' } })
}

async function removeAccount(account) {
  try {
    await ElMessageBox.confirm(
      `仅从本机移除账号“${account.user.displayName || account.user.username}”的登录记录，是否继续？`,
      '移除账号',
      { type: 'warning', confirmButtonText: '移除', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  auth.removeAccount(account.user.id)
  ElMessage.success('已从本机移除该账号')
}

function logout() {
  auth.logout()
  router.replace('/login')
}
</script>

<style scoped>
.profile-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.15fr) minmax(0, 0.85fr);
  gap: 16px;
  align-items: start;
}

.profile-card {
  overflow: hidden;
  grid-row: span 2;
}

.profile-cover {
  height: 92px;
  background:
    radial-gradient(circle at 20% 20%, rgba(34, 211, 238, 0.34), transparent 55%),
    linear-gradient(120deg, rgba(34, 211, 238, 0.18), rgba(59, 130, 246, 0.22), rgba(124, 58, 237, 0.16));
}

.profile-identity {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 0 22px;
  margin-top: -26px;
}

.profile-avatar {
  width: 64px;
  height: 64px;
  border-radius: 18px;
  border: 3px solid #0d1728;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #22d3ee, #3b82f6);
  color: #06121f;
  font-size: 25px;
  font-weight: 800;
  flex: none;
}

.profile-name {
  flex: 1;
  min-width: 0;
  padding-top: 22px;
}

.profile-name h2 {
  margin: 0;
  font-size: 22px;
}

.profile-name p {
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--qts-text-muted);
}

.profile-meta {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
  padding: 22px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding: 12px;
  border-radius: 10px;
  background: rgba(148, 163, 184, 0.05);
  border: 1px solid rgba(148, 163, 184, 0.1);
}

.meta-item span {
  font-size: 11px;
  color: var(--qts-text-muted);
}

.meta-item b {
  font-size: 13px;
  font-weight: 600;
  word-break: break-all;
}

.online {
  display: flex;
  align-items: center;
  gap: 6px;
  color: #34d399;
}

.online i {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #34d399;
  box-shadow: 0 0 8px rgba(52, 211, 153, 0.8);
}

.accounts-card {
  grid-column: span 2;
}

.account-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.account-row {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 10px 12px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.14);
  background: rgba(148, 163, 184, 0.04);
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition: border-color 0.16s ease, background 0.16s ease;
}

.account-row:hover {
  border-color: rgba(34, 211, 238, 0.45);
  background: rgba(34, 211, 238, 0.06);
}

.account-row.current {
  border-color: rgba(34, 211, 238, 0.5);
  background: rgba(34, 211, 238, 0.07);
  cursor: default;
}

.account-row:disabled {
  opacity: 0.7;
}

.account-avatar {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #22d3ee, #3b82f6);
  color: #06121f;
  font-weight: 700;
  flex: none;
}

.account-info {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.account-info small {
  color: var(--qts-text-muted);
  font-size: 11px;
}

.accounts-hint {
  margin: 14px 0 0;
  font-size: 11px;
  line-height: 1.7;
  color: var(--qts-text-muted);
}

.session-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.session-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 9px 0;
  border-bottom: 1px dashed rgba(148, 163, 184, 0.12);
  font-size: 12px;
  color: var(--qts-text-muted);
}

.session-row b {
  color: var(--qts-text);
  font-weight: 600;
}

.session-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  margin: 8px 0;
}

@media (max-width: 1100px) {
  .profile-grid {
    grid-template-columns: 1fr;
  }
  .profile-card,
  .accounts-card {
    grid-column: auto;
    grid-row: auto;
  }
}
</style>
