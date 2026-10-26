<template>
  <div class="login-page">
    <div class="login-ambient" aria-hidden="true"></div>

    <section class="login-brand">
      <div class="brand-head">
        <div class="brand-mark-lg">
          <el-icon><DataLine /></el-icon>
        </div>
        <div class="brand-head-text">
          <span class="login-brand-name">QTS</span>
          <span class="login-brand-sub">量化交易系统</span>
        </div>
      </div>

      <div class="brand-copy">
        <p class="brand-kicker">OKX STRATEGY TERMINAL</p>
        <h1>
          让每一次交易<br />
          <span>都有数据支撑</span>
        </h1>
        <p class="brand-desc">
          OKX 策略回测与自动交易终端。从历史回测、纸面验证到实盘执行，全流程可控、风险可量化。
        </p>

        <ul class="brand-points">
          <li>
            <span class="point-icon"><el-icon><CircleCheckFilled /></el-icon></span>
            <div>
              <strong>完整的策略验证闭环</strong>
              <span>回测 → 模拟盘 → 实盘，一套界面完成全部流程</span>
            </div>
          </li>
          <li>
            <span class="point-icon"><el-icon><CircleCheckFilled /></el-icon></span>
            <div>
              <strong>内置交易风控</strong>
              <span>止损止盈、仓位上限、OKX API 账户独立核算</span>
            </div>
          </li>
          <li>
            <span class="point-icon"><el-icon><CircleCheckFilled /></el-icon></span>
            <div>
              <strong>深度对接 OKX 行情</strong>
              <span>K 线、账户与持仓实时同步，断网自动降级示例数据</span>
            </div>
          </li>
        </ul>
      </div>

      <div class="login-radar" aria-hidden="true">
        <div class="radar-grid"></div>
        <span class="radar-ring ring-1"></span>
        <span class="radar-ring ring-2"></span>
        <span class="radar-ring ring-3"></span>
        <span class="radar-ring ring-4"></span>
        <div class="radar-sweep"></div>
        <span class="radar-blip blip-1"></span>
        <span class="radar-blip blip-2"></span>
        <span class="radar-blip blip-3"></span>
        <span class="radar-core"></span>
      </div>

      <footer class="brand-footer">
        <span>© 2026 QTS Quant Trading System</span>
        <span class="brand-footer-risk">投资有风险 · 入市需谨慎</span>
      </footer>
    </section>

    <section class="login-panel">
      <div class="login-card">
        <div class="login-mobile-brand">
          <div class="brand-mark-lg">
            <el-icon><DataLine /></el-icon>
          </div>
          <span>QTS · 量化交易系统</span>
        </div>

        <div class="login-card-head">
          <h2>{{ isAddMode ? '添加账号' : '欢迎回来' }}</h2>
          <p>{{ isAddMode ? '登录另一个账号，保存后可在顶部菜单快速切换' : '登录 QTS 量化交易终端，管理你的策略与任务' }}</p>
        </div>

        <el-alert
          v-if="errorMessage"
          type="error"
          :closable="false"
          show-icon
          :title="errorMessage"
          class="login-error"
        />

        <el-form
          ref="formRef"
          :model="form"
          :rules="rules"
          size="large"
          class="login-form"
          @submit.prevent="submit"
        >
          <el-form-item prop="username">
            <el-input
              v-model="form.username"
              placeholder="用户名"
              :prefix-icon="User"
              autocomplete="username"
              @keyup.enter="submit"
            />
          </el-form-item>
          <el-form-item prop="password">
            <el-input
              v-model="form.password"
              type="password"
              show-password
              placeholder="密码"
              :prefix-icon="Lock"
              autocomplete="current-password"
              @keyup.enter="submit"
            />
          </el-form-item>
          <el-button
            class="login-submit"
            type="primary"
            size="large"
            native-type="submit"
            :loading="auth.pending"
          >
            登录交易终端
          </el-button>
        </el-form>

        <div class="login-alt">
          <router-link :to="{ path: '/register', query: isAddMode ? { add: '1' } : {} }">注册新账号</router-link>
          <template v-if="isAddMode">
            <span class="login-alt-dot">·</span>
            <router-link to="/">返回当前账号</router-link>
          </template>
        </div>

        <div v-if="auth.accounts.length" class="quick-accounts">
          <div class="quick-accounts-title">已保存账号 · 点击直接切换</div>
          <button
            v-for="account in auth.accounts"
            :key="account.user.id"
            type="button"
            class="quick-account"
            :disabled="auth.pending"
            @click="quickSwitch(account)"
          >
            <span class="quick-account-avatar">{{ avatarOf(account.user) }}</span>
            <span class="quick-account-meta">
              <b>{{ account.user.displayName || account.user.username }}</b>
              <small>@{{ account.user.username }}</small>
            </span>
            <el-icon><Right /></el-icon>
          </button>
        </div>

        <div class="login-hint">
          <el-icon><InfoFilled /></el-icon>
          <span>默认演示账号：<b>admin</b> / <b>admin123</b></span>
        </div>

        <div class="login-meta">
          <span>v1.0.0</span>
          <span class="login-meta-dot">·</span>
          <span>QTS Quant Trading System</span>
        </div>
      </div>

      <p class="login-disclaimer">本系统仅用于策略研究与交易辅助，不构成任何投资建议。</p>
    </section>
  </div>
</template>

<script setup>
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Lock, User } from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const formRef = ref(null)
const errorMessage = ref('')
const isAddMode = computed(() => route.query?.add === '1')
const form = reactive({
  username: 'admin',
  password: ''
})

const rules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }]
}

function avatarOf(accountUser) {
  const name = accountUser?.displayName || accountUser?.username || 'Q'
  return String(name).trim().slice(0, 1).toUpperCase()
}

async function quickSwitch(account) {
  errorMessage.value = ''
  try {
    if (String(account.user.id) === String(auth.user?.id)) {
      router.replace('/')
      return
    }
    await auth.switchAccount(account.user.id)
    ElMessage.success(`已切换到 ${auth.displayName}`)
    router.replace('/')
  } catch (error) {
    errorMessage.value = error?.message || '该账号登录已失效，请重新输入密码'
    form.username = account?.user?.username || form.username
  }
}

async function submit() {
  if (!formRef.value) return
  try {
    await formRef.value.validate()
  } catch {
    return
  }
  errorMessage.value = ''
  try {
    await auth.login({ username: form.username.trim(), password: form.password })
    ElMessage.success('登录成功，欢迎回来')
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    router.replace(redirect && redirect.startsWith('/') ? redirect : '/')
  } catch (error) {
    errorMessage.value = error?.message || '登录失败，请稍后重试'
  }
}
</script>

<style scoped>
.login-page {
  position: relative;
  min-height: 100vh;
  display: flex;
  background: var(--qts-bg);
  overflow: hidden;
}

.login-ambient {
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background:
    radial-gradient(880px 520px at 18% -8%, rgba(34, 211, 238, 0.1), transparent 62%),
    radial-gradient(760px 460px at 96% 4%, rgba(99, 102, 241, 0.12), transparent 60%);
}

/* ---------------- 左侧品牌区 ---------------- */
.login-brand {
  position: relative;
  z-index: 1;
  width: 56%;
  max-width: 860px;
  min-height: 100vh;
  padding: 44px 56px 32px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-right: 1px solid var(--qts-border);
  background:
    linear-gradient(158deg, rgba(15, 27, 46, 0.96) 0%, rgba(7, 12, 22, 0.98) 58%, rgba(6, 11, 20, 1) 100%);
}

.brand-head {
  display: flex;
  align-items: center;
  gap: 13px;
  position: relative;
  z-index: 2;
}

.brand-mark-lg {
  width: 44px;
  height: 44px;
  border-radius: 13px;
  display: grid;
  place-items: center;
  font-size: 22px;
  color: #67e8f9;
  background: linear-gradient(135deg, rgba(34, 211, 238, 0.22), rgba(99, 102, 241, 0.24));
  border: 1px solid rgba(34, 211, 238, 0.42);
  box-shadow: 0 0 26px rgba(34, 211, 238, 0.2);
}

.brand-head-text {
  display: flex;
  flex-direction: column;
  line-height: 1.2;
}

.login-brand-name {
  font-size: 21px;
  font-weight: 800;
  letter-spacing: 1px;
  color: #f8fafc;
}

.login-brand-sub {
  font-size: 11px;
  letter-spacing: 2.4px;
  color: var(--qts-text-dim);
}

.brand-copy {
  position: relative;
  z-index: 2;
  margin-top: auto;
  margin-bottom: auto;
  max-width: 520px;
  padding: 40px 0;
}

.brand-kicker {
  font-size: 11px;
  letter-spacing: 3.5px;
  color: rgba(34, 211, 238, 0.75);
  margin-bottom: 18px;
  font-family: var(--qts-mono);
}

.brand-copy h1 {
  font-size: 42px;
  line-height: 1.28;
  font-weight: 700;
  letter-spacing: 1px;
  color: #f1f5f9;
}

.brand-copy h1 span {
  background: linear-gradient(90deg, #67e8f9 0%, #818cf8 100%);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
}

.brand-desc {
  margin-top: 18px;
  max-width: 460px;
  font-size: 14px;
  line-height: 1.85;
  color: var(--qts-text-muted);
}

.brand-points {
  margin-top: 34px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.brand-points li {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.point-icon {
  display: grid;
  place-items: center;
  width: 24px;
  height: 24px;
  margin-top: 2px;
  border-radius: 8px;
  color: var(--qts-accent);
  background: rgba(34, 211, 238, 0.1);
  border: 1px solid rgba(34, 211, 238, 0.24);
  font-size: 13px;
  flex-shrink: 0;
}

.brand-points strong {
  display: block;
  font-size: 13.5px;
  font-weight: 600;
  color: #e2e8f0;
  margin-bottom: 2px;
}

.brand-points span {
  font-size: 12.5px;
  color: var(--qts-text-dim);
  line-height: 1.6;
}

/* 雷达背景 */
.login-radar {
  position: absolute;
  right: -190px;
  bottom: -210px;
  width: 620px;
  height: 620px;
  border-radius: 50%;
  pointer-events: none;
  z-index: 1;
}

.radar-grid {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  background-image:
    linear-gradient(rgba(34, 211, 238, 0.07) 1px, transparent 1px),
    linear-gradient(90deg, rgba(34, 211, 238, 0.07) 1px, transparent 1px);
  background-size: 56px 56px;
  -webkit-mask-image: radial-gradient(circle, #000 0%, #000 58%, transparent 72%);
  mask-image: radial-gradient(circle, #000 0%, #000 58%, transparent 72%);
}

.radar-ring {
  position: absolute;
  border-radius: 50%;
  border: 1px solid rgba(34, 211, 238, 0.14);
}

.ring-1 {
  inset: 0;
}

.ring-2 {
  inset: 15%;
}

.ring-3 {
  inset: 31%;
}

.ring-4 {
  inset: 46%;
  border-color: rgba(34, 211, 238, 0.24);
}

.radar-sweep {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  background: conic-gradient(
    from 0deg,
    rgba(34, 211, 238, 0) 0deg,
    rgba(34, 211, 238, 0) 288deg,
    rgba(34, 211, 238, 0.05) 320deg,
    rgba(34, 211, 238, 0.22) 356deg,
    rgba(34, 211, 238, 0.4) 360deg
  );
  -webkit-mask-image: radial-gradient(circle, transparent 8%, #000 9%);
  mask-image: radial-gradient(circle, transparent 8%, #000 9%);
  animation: radar-spin 7s linear infinite;
}

.radar-core {
  position: absolute;
  left: 50%;
  top: 50%;
  width: 9px;
  height: 9px;
  margin: -4.5px 0 0 -4.5px;
  border-radius: 50%;
  background: #67e8f9;
  box-shadow: 0 0 18px 4px rgba(34, 211, 238, 0.5);
}

.radar-blip {
  position: absolute;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: rgba(103, 232, 249, 0.9);
  box-shadow: 0 0 10px 2px rgba(34, 211, 238, 0.45);
  animation: blip 3.2s ease-in-out infinite;
}

.blip-1 {
  left: 64%;
  top: 27%;
  animation-delay: 0.2s;
}

.blip-2 {
  left: 33%;
  top: 58%;
  animation-delay: 1.1s;
}

.blip-3 {
  left: 72%;
  top: 62%;
  animation-delay: 2.2s;
}

@keyframes radar-spin {
  to {
    transform: rotate(360deg);
  }
}

@keyframes blip {
  0%,
  100% {
    opacity: 0.25;
    transform: scale(0.8);
  }
  45% {
    opacity: 1;
    transform: scale(1.25);
  }
}

.brand-footer {
  position: relative;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 11px;
  color: var(--qts-text-dim);
  letter-spacing: 0.3px;
}

.brand-footer-risk {
  color: rgba(251, 191, 36, 0.72);
}

/* ---------------- 右侧登录区 ---------------- */
.login-panel {
  position: relative;
  z-index: 1;
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px;
}

.login-card {
  width: 100%;
  max-width: 404px;
  padding: 34px 32px 26px;
  border-radius: 18px;
  border: 1px solid var(--qts-border);
  background: linear-gradient(180deg, rgba(17, 26, 46, 0.94) 0%, rgba(11, 18, 32, 0.97) 100%);
  box-shadow:
    0 40px 90px -50px rgba(0, 0, 0, 0.95),
    0 0 0 1px rgba(255, 255, 255, 0.015) inset;
}

.login-mobile-brand {
  display: none;
  align-items: center;
  gap: 10px;
  margin-bottom: 22px;
  font-size: 15px;
  font-weight: 700;
  color: #f1f5f9;
}

.login-card-head h2 {
  font-size: 24px;
  font-weight: 700;
  color: #f8fafc;
  letter-spacing: 0.3px;
}

.login-card-head p {
  margin-top: 7px;
  font-size: 12.5px;
  color: var(--qts-text-dim);
}

.login-error {
  margin-top: 18px;
}

.login-form {
  margin-top: 22px;
}

.login-form :deep(.el-form-item) {
  margin-bottom: 18px;
}

.login-form :deep(.el-input__wrapper) {
  height: 46px;
  border-radius: 11px;
}

.login-form :deep(.el-input__prefix) {
  color: var(--qts-text-dim);
}

.login-submit {
  width: 100%;
  height: 46px;
  margin-top: 4px;
  border-radius: 11px;
  font-size: 15px;
  letter-spacing: 1px;
}

.login-alt {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin-top: 14px;
  font-size: 12px;
  color: var(--qts-text-muted);
}

.login-alt a {
  color: var(--qts-accent);
  text-decoration: none;
}

.login-alt a:hover {
  text-decoration: underline;
}

.login-alt-dot {
  opacity: 0.5;
}

.quick-accounts {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid rgba(148, 163, 184, 0.12);
}

.quick-accounts-title {
  font-size: 11px;
  letter-spacing: 0.5px;
  color: var(--qts-text-muted);
  margin-bottom: 2px;
}

.quick-account {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 9px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  border-radius: 9px;
  background: rgba(148, 163, 184, 0.04);
  color: var(--qts-text);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.16s ease, background 0.16s ease;
}

.quick-account:hover {
  border-color: rgba(34, 211, 238, 0.5);
  background: rgba(34, 211, 238, 0.07);
}

.quick-account:disabled {
  opacity: 0.6;
  cursor: wait;
}

.quick-account-avatar {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: linear-gradient(135deg, #22d3ee, #3b82f6);
  color: #06121f;
  font-size: 12px;
  font-weight: 700;
  flex: none;
}

.quick-account-meta {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
  line-height: 1.25;
}

.quick-account-meta small {
  color: var(--qts-text-muted);
  font-size: 10px;
}

.quick-account > .el-icon {
  color: var(--qts-text-muted);
}

.login-hint {
  display: flex;
  align-items: center;
  gap: 7px;
  margin-top: 20px;
  padding: 10px 12px;
  border-radius: 10px;
  border: 1px dashed rgba(148, 163, 184, 0.2);
  background: rgba(148, 163, 184, 0.04);
  font-size: 12px;
  color: var(--qts-text-muted);
}

.login-hint .el-icon {
  color: var(--qts-accent);
}

.login-hint b {
  color: #67e8f9;
  font-family: var(--qts-mono);
  font-weight: 600;
}

.login-meta {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  margin-top: 18px;
  font-size: 10.5px;
  color: var(--qts-text-dim);
  letter-spacing: 0.4px;
}

.login-meta-dot {
  opacity: 0.5;
}

.login-disclaimer {
  margin-top: 22px;
  font-size: 11px;
  color: rgba(100, 116, 139, 0.9);
  text-align: center;
  max-width: 380px;
  line-height: 1.7;
}

@media (max-width: 1180px) {
  .login-brand {
    width: 50%;
    padding: 36px 36px 28px;
  }

  .brand-copy h1 {
    font-size: 34px;
  }
}

@media (max-width: 1024px) {
  .login-brand {
    display: none;
  }

  .login-panel {
    padding: 28px 18px;
  }

  .login-mobile-brand {
    display: flex;
  }
}

@media (max-width: 480px) {
  .login-card {
    padding: 26px 20px 20px;
    border-radius: 15px;
  }

  .login-card-head h2 {
    font-size: 21px;
  }
}
</style>
