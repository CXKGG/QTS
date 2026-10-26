<template>
  <div class="register-page">
    <div class="register-glow register-glow--a"></div>
    <div class="register-glow register-glow--b"></div>

    <section class="register-brand">
      <div class="brand-mark"><el-icon><DataLine /></el-icon></div>
      <div>
        <div class="brand-title">QTS</div>
        <div class="brand-sub">Quant Trading System</div>
      </div>
      <h1>创建你的交易终端账号</h1>
      <p>每个账号拥有独立的个人资料与会话；OKX API 账户由系统统一接入，多账号共享同一交易账户。</p>
      <ul>
        <li><el-icon><CircleCheckFilled /></el-icon> 注册后自动登录，无需重复输入密码</li>
        <li><el-icon><CircleCheckFilled /></el-icon> 个人中心可修改昵称、邮箱与密码</li>
        <li><el-icon><CircleCheckFilled /></el-icon> 顶部菜单快速切换已保存账号</li>
      </ul>
    </section>

    <section class="register-panel">
      <div class="register-card">
        <div class="register-head">
          <h2>注册账号</h2>
          <p>创建后立即进入量化交易终端</p>
        </div>

        <el-alert v-if="errorMessage" type="error" :closable="false" show-icon :title="errorMessage" class="register-error" />

        <el-form ref="formRef" :model="form" :rules="rules" label-position="top" size="large" @submit.prevent="submit">
          <el-form-item label="用户名" prop="username">
            <el-input v-model="form.username" placeholder="3-32 位字母、数字、下划线或中文" :prefix-icon="User" autocomplete="username" />
          </el-form-item>
          <el-form-item label="昵称" prop="displayName">
            <el-input v-model="form.displayName" placeholder="用于界面展示，可留空" :prefix-icon="Postcard" maxlength="32" />
          </el-form-item>
          <el-form-item label="邮箱（可选）" prop="email">
            <el-input v-model="form.email" placeholder="用于账号识别，不会公开" :prefix-icon="Message" autocomplete="email" />
          </el-form-item>
          <el-form-item label="密码" prop="password">
            <el-input v-model="form.password" type="password" show-password placeholder="6-64 位" :prefix-icon="Lock" autocomplete="new-password" />
          </el-form-item>
          <el-form-item label="确认密码" prop="confirmPassword">
            <el-input v-model="form.confirmPassword" type="password" show-password placeholder="再次输入密码" :prefix-icon="Lock" autocomplete="new-password" @keyup.enter="submit" />
          </el-form-item>
          <el-button class="register-submit" type="primary" size="large" native-type="submit" :loading="auth.pending">
            创建账号并登录
          </el-button>
        </el-form>

        <div class="register-alt">
          已有账号？
          <router-link :to="{ path: '/login', query: isAddMode ? { add: '1' } : {} }">返回登录</router-link>
        </div>
      </div>
      <p class="register-disclaimer">注册即表示你已了解量化交易与合约杠杆风险，系统不构成投资建议。</p>
    </section>
  </div>
</template>

<script setup>
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { DataLine, Lock, Message, Postcard, User, CircleCheckFilled } from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const isAddMode = computed(() => route.query?.add === '1')

const formRef = ref(null)
const errorMessage = ref('')
const form = reactive({
  username: '',
  displayName: '',
  email: '',
  password: '',
  confirmPassword: ''
})

const rules = {
  username: [
    { required: true, message: '请输入用户名', trigger: 'blur' },
    { pattern: /^[\p{L}\p{N}_-]{3,32}$/u, message: '3-32 位字母、数字、下划线、短横线或中文，不能有空格', trigger: 'blur' }
  ],
  displayName: [{ max: 32, message: '昵称不能超过 32 个字符', trigger: 'blur' }],
  email: [{ type: 'email', message: '请输入有效的邮箱地址', trigger: 'blur' }],
  password: [
    { required: true, message: '请输入密码', trigger: 'blur' },
    { min: 6, max: 64, message: '密码长度需为 6-64 位', trigger: 'blur' }
  ],
  confirmPassword: [
    { required: true, message: '请再次输入密码', trigger: 'blur' },
    {
      validator: (_rule, value, callback) => {
        if (value !== form.password) callback(new Error('两次输入的密码不一致'))
        else callback()
      },
      trigger: 'blur'
    }
  ]
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
    await auth.register({
      username: form.username.trim(),
      displayName: form.displayName.trim(),
      email: form.email.trim() || null,
      password: form.password
    })
    ElMessage.success('注册成功，已自动登录')
    router.replace('/')
  } catch (error) {
    errorMessage.value = error?.message || '注册失败，请稍后重试'
  }
}
</script>

<style scoped>
.register-page {
  position: relative;
  min-height: 100vh;
  display: grid;
  grid-template-columns: 1fr 1fr;
  background: var(--qts-bg, #070d18);
  overflow: hidden;
}

.register-glow {
  position: absolute;
  width: 520px;
  height: 520px;
  border-radius: 50%;
  filter: blur(90px);
  pointer-events: none;
  opacity: 0.32;
}

.register-glow--a {
  top: -220px;
  left: -160px;
  background: #0ea5e9;
}

.register-glow--b {
  bottom: -260px;
  right: -120px;
  background: #7c3aed;
}

.register-brand {
  position: relative;
  z-index: 1;
  padding: 64px 72px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  border-right: 1px solid rgba(148, 163, 184, 0.1);
}

.brand-mark {
  width: 48px;
  height: 48px;
  display: grid;
  place-items: center;
  border-radius: 13px;
  background: linear-gradient(135deg, #22d3ee, #3b82f6);
  color: #06121f;
  margin-bottom: 14px;
}

.brand-title {
  font-size: 26px;
  font-weight: 800;
  letter-spacing: 3px;
}

.brand-sub {
  font-size: 11px;
  letter-spacing: 1.6px;
  color: var(--qts-text-muted, #8090a7);
}

.register-brand h1 {
  margin: 54px 0 16px;
  font-size: 36px;
  line-height: 1.3;
  letter-spacing: -0.5px;
}

.register-brand p {
  max-width: 430px;
  color: var(--qts-text-muted, #8090a7);
  line-height: 1.8;
}

.register-brand ul {
  list-style: none;
  padding: 0;
  margin: 32px 0 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
  color: var(--qts-text, #dbe6f5);
}

.register-brand li {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 14px;
}

.register-brand li .el-icon {
  color: #34d399;
}

.register-panel {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 40px;
}

.register-card {
  width: min(460px, 100%);
  padding: 34px 36px;
  border-radius: 18px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background: rgba(15, 25, 43, 0.82);
  box-shadow: 0 26px 80px rgba(2, 8, 23, 0.55);
  backdrop-filter: blur(16px);
}

.register-head h2 {
  margin: 0;
  font-size: 25px;
}

.register-head p {
  margin: 8px 0 22px;
  color: var(--qts-text-muted, #8090a7);
  font-size: 13px;
}

.register-error {
  margin-bottom: 14px;
}

.register-submit {
  width: 100%;
  height: 46px;
  margin-top: 6px;
  border-radius: 11px;
  font-size: 15px;
  letter-spacing: 1px;
}

.register-alt {
  margin-top: 20px;
  text-align: center;
  font-size: 12px;
  color: var(--qts-text-muted, #8090a7);
}

.register-alt a {
  color: var(--qts-accent, #22d3ee);
  text-decoration: none;
}

.register-disclaimer {
  margin-top: 20px;
  font-size: 11px;
  color: var(--qts-text-muted, #8090a7);
}

:deep(.el-form-item__label) {
  color: var(--qts-text-muted, #8090a7);
  font-size: 12px;
}

@media (max-width: 980px) {
  .register-page {
    grid-template-columns: 1fr;
  }
  .register-brand {
    display: none;
  }
}
</style>
