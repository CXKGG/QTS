<template>
  <div class="page-stack">
    <PageHeader title="系统设置" subtitle="OKX API 凭据、真实账户与运行环境">
      <el-button :icon="Refresh" :loading="refreshing" @click="refreshAll">刷新</el-button>
    </PageHeader>

    <div class="settings-layout">
      <div class="settings-main">
        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">欧易 (OKX) API</div>
            <div class="card-extra">
              <el-tag v-if="okx.connected" size="small" type="success" effect="plain">已连接</el-tag>
              <el-tag v-else-if="okx.hasApiKey" size="small" type="warning" effect="plain">连接失败</el-tag>
              <el-tag v-else size="small" type="info" effect="plain">未配置</el-tag>
            </div>
          </div>

          <div v-if="okxLoading && !okxLoaded" class="card-body">
            <el-skeleton :rows="7" animated />
          </div>

          <div v-else class="card-body">
            <el-alert
              v-if="okx.connectionError"
              class="settings-alert"
              type="error"
              :title="okx.connectionError"
              :closable="false"
              show-icon
            />

            <el-form label-position="top" class="settings-form" @submit.prevent="saveOkx">
              <el-form-item label="API 地址">
                <el-input v-model="form.baseUrl" placeholder="https://www.okx.com" />
                <div class="field-hint">当前经本机 TLS 代理访问欧易（start.bat 自动启动）；留空恢复 https://www.okx.com。</div>
              </el-form-item>

              <div class="form-grid-2">
                <el-form-item label="API Key">
                  <el-input
                    v-model="form.apiKey"
                    :placeholder="okx.apiKeyMasked || (okx.hasApiKey ? '已保存（留空不修改）' : '尚未配置')"
                    show-password
                    autocomplete="off"
                  />
                </el-form-item>
                <el-form-item label="Passphrase">
                  <el-input
                    v-model="form.passphrase"
                    :placeholder="okx.hasPassphrase ? '已保存（留空不修改）' : 'API 密码短语'"
                    show-password
                    autocomplete="off"
                  />
                </el-form-item>
              </div>

              <el-form-item label="Secret Key">
                <el-input
                  v-model="form.secretKey"
                  :placeholder="okx.hasSecretKey ? '已保存（留空不修改）' : '尚未配置'"
                  show-password
                  autocomplete="off"
                />
                <div class="field-hint">出于安全考虑，已保存的密钥不会回显；留空表示保持原有密钥不变。</div>
              </el-form-item>

              <div class="switch-list">
                <div class="switch-item">
                  <div class="switch-copy">
                    <b>OKX 模拟环境</b>
                    <span>使用 OKX 演示账户下单，不影响真实资金；真实账户请关闭此项</span>
                  </div>
                  <el-switch v-model="form.simulatedTrading" />
                </div>
                <div class="switch-item">
                  <div class="switch-copy">
                    <b>允许实盘交易</b>
                    <span>{{ form.simulatedTrading ? '当前使用 OKX 演示环境：Live 任务也只会发往演示账户，不会动用真实资金' : '当前使用 OKX 真实环境：Live 模式任务会使用该 API 账户的真实资金下单，请谨慎操作' }}</span>
                  </div>
                  <el-switch v-model="form.liveTradingEnabled" @change="onLiveToggle" />
                </div>
              </div>

              <el-alert
                v-if="form.simulatedTrading && form.liveTradingEnabled"
                class="settings-alert"
                type="warning"
                :closable="false"
                show-icon
                title="OKX 模拟环境与实盘开关同时开启"
                description="欧易请求仍会携带 x-simulated-trading:1，Live 任务不会使用真实资金。确认真实下单前，请先关闭 OKX 模拟环境并重新测试连接。"
              />

              <el-alert v-if="formError" class="settings-alert" type="error" :title="formError" :closable="false" show-icon />

              <div class="settings-actions">
                <el-button :icon="Connection" :loading="testing" @click="testConnection">测试连接</el-button>
                <el-button type="primary" :icon="Check" :loading="saving" native-type="submit" @click="saveOkx">
                  保存配置
                </el-button>
              </div>
            </el-form>

            <el-alert
              v-if="testResult"
              class="settings-alert test-result"
              :type="testResult.success ? 'success' : 'error'"
              :title="testResult.message || (testResult.success ? '连接成功' : '连接失败')"
              :closable="false"
              show-icon
            >
              <template v-if="testResult.account">
                账户资产：{{ fmtMoney(testResult.account.totalEquity) }} {{ testResult.account.currency || 'USDT' }}
              </template>
            </el-alert>

            <div class="okx-foot">
              <span>数据源：<b>{{ okx.dataSource === 'synthetic' ? '示例数据回退' : 'OKX' }}</b></span>
              <span>最近保存：{{ fmtDateTime(okx.savedAt) }}</span>
            </div>
          </div>
        </section>

        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">AI 决策引擎</div>
            <div class="card-extra">
              <el-tag v-if="ai.llmConfigured" size="small" type="success" effect="plain">大模型已启用</el-tag>
              <el-tag v-else size="small" type="info" effect="plain">本地量化引擎</el-tag>
            </div>
          </div>
          <div v-if="aiLoading && !aiLoaded" class="card-body">
            <el-skeleton :rows="5" animated />
          </div>
          <div v-else class="card-body">
            <p class="settings-desc">
              不配置大模型时使用完全离线的 EMA/MACD/RSI/ATR 多因子引擎，同样可以生成策略并一键回测。
              接入 DeepSeek、OpenAI、Kimi、通义、智谱等 OpenAI 兼容服务后，会结合本地指标生成方案，失败自动回退。
            </p>
            <el-form label-position="top" class="settings-form" @submit.prevent="saveAi">
              <div class="switch-list">
                <div class="switch-item">
                  <div class="switch-copy">
                    <b>启用大模型增强</b>
                    <span>停用时只使用离线本地引擎，不产生任何外部请求</span>
                  </div>
                  <el-switch v-model="aiForm.enabled" />
                </div>
              </div>
              <div class="form-grid-2">
                <el-form-item label="模型服务">
                  <el-select
                    v-model="aiForm.providerType"
                    style="width: 100%"
                    placeholder="选择模型服务"
                    @change="onAiProviderChange"
                  >
                    <el-option
                      v-for="provider in aiProviders"
                      :key="provider.key"
                      :label="provider.label"
                      :value="provider.key"
                    />
                  </el-select>
                </el-form-item>
                <el-form-item label="Base URL">
                  <el-input v-model="aiForm.baseUrl" placeholder="https://api.deepseek.com/v1" />
                </el-form-item>
              </div>
              <div class="form-grid-2">
                <el-form-item label="模型名称">
                  <el-select
                    v-model="aiForm.model"
                    style="width: 100%"
                    filterable
                    allow-create
                    default-first-option
                    placeholder="选择或输入模型名"
                  >
                    <el-option v-for="model in aiModelOptions" :key="model" :label="model" :value="model" />
                  </el-select>
                </el-form-item>
                <el-form-item label="请求超时（秒）">
                  <el-input-number v-model="aiForm.timeoutSeconds" :min="5" :max="120" :step="5" controls-position="right" />
                </el-form-item>
              </div>
              <el-form-item label="API Key">
                <el-input
                  v-model="aiForm.apiKey"
                  :placeholder="ai.apiKeyMasked || (ai.hasApiKey ? '已保存（留空不修改）' : '尚未配置，留空则使用本地引擎')"
                  show-password
                  autocomplete="off"
                />
                <div class="field-hint">
                  密钥仅加密保存在本机数据库，接口不会回显。可选 DeepSeek（deepseek-chat / deepseek-reasoner）、
                  OpenAI、Kimi、通义千问、智谱 GLM，或任意兼容 /chat/completions 的自定义服务。
                </div>
              </el-form-item>
              <el-alert v-if="aiError" class="settings-alert" type="error" :title="aiError" :closable="false" show-icon />
              <div class="settings-actions">
                <el-button type="primary" :icon="Check" :loading="aiSaving" native-type="submit">保存 AI 配置</el-button>
                <el-button :icon="Refresh" :loading="aiLoading" @click="loadAi">刷新状态</el-button>
              </div>
            </el-form>
            <div class="okx-foot">
              <span>当前模式：<b>{{ ai.provider || '本地量化引擎' }}</b></span>
              <span>{{ ai.message || '无需外网即可生成策略' }}</span>
            </div>
          </div>
        </section>

        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">K 线配色</div>
            <div class="card-extra"><span class="text-dim">本机生效</span></div>
          </div>
          <div class="card-body">
            <p class="settings-desc">
              选择“红涨绿跌”或“绿涨红跌”，也可以自定义涨跌颜色。保存后行情、交易台与 AI 决策页的 K 线立即同步。
            </p>
            <div class="color-preset-row">
              <button
                v-for="preset in klinePresets"
                :key="preset.key"
                type="button"
                class="color-preset"
                :class="{ active: activeKlinePreset === preset.key }"
                @click="applyKlinePreset(preset)"
              >
                <span class="color-swatch" :style="{ background: preset.up }"></span>
                <span class="color-swatch" :style="{ background: preset.down }"></span>
                <span class="color-preset-copy">
                  <b>{{ preset.label }}</b>
                  <small>{{ preset.hint }}</small>
                </span>
              </button>
            </div>
            <div class="form-grid-2 color-picker-row">
              <el-form-item label="上涨颜色">
                <div class="color-picker-line">
                  <el-color-picker v-model="klineColors.up" />
                  <span class="color-code">{{ klineColors.up }}</span>
                  <span class="text-dim">阳线 / 上涨</span>
                </div>
              </el-form-item>
              <el-form-item label="下跌颜色">
                <div class="color-picker-line">
                  <el-color-picker v-model="klineColors.down" />
                  <span class="color-code">{{ klineColors.down }}</span>
                  <span class="text-dim">阴线 / 下跌</span>
                </div>
              </el-form-item>
            </div>
            <div class="settings-actions">
              <el-button type="primary" :icon="Check" @click="saveKlinePalette">保存 K 线配色</el-button>
              <el-button :icon="RefreshLeft" @click="resetKlinePalette">恢复默认</el-button>
            </div>
            <div class="field-hint">配色保存在当前浏览器；重置浏览器数据后会恢复默认“绿涨红跌”。</div>
          </div>
        </section>
      </div>

      <div class="settings-side">
        <section class="qts-card">
          <div class="card-head">
            <div class="card-title">系统状态</div>
            <div class="card-extra">
              <el-button link type="primary" :loading="systemLoading" @click="loadSystem">刷新</el-button>
            </div>
          </div>
          <div v-if="systemLoading && !system" class="card-body">
            <el-skeleton :rows="7" animated />
          </div>
          <div v-else class="card-body">
            <div v-if="systemError" class="settings-alert">
              <el-alert :title="systemError" type="error" :closable="false" show-icon />
            </div>
            <div v-else class="kv-list">
              <div class="kv-row">
                <span>系统版本</span>
                <b class="num">{{ system?.version || '—' }}</b>
              </div>
              <div class="kv-row">
                <span>数据库</span>
                <b>{{ system?.database || '—' }}</b>
              </div>
              <div class="kv-row">
                <span>MySQL 版本</span>
                <b class="num">{{ system?.mysqlVersion || '—' }}</b>
              </div>
              <div class="kv-row">
                <span>K 线数据量</span>
                <b class="num">{{ fmtCompact(system?.klineCount) }} 根</b>
              </div>
              <div class="kv-row">
                <span>回测记录</span>
                <b class="num">{{ fmtCompact(system?.backtestCount) }} 条</b>
              </div>
              <div class="kv-row">
                <span>策略任务</span>
                <b class="num">{{ fmtCompact(system?.taskCount) }} 个</b>
              </div>
              <div class="kv-row">
                <span>服务器时间</span>
                <b class="num">{{ fmtDateTime(system?.serverTime) }}</b>
              </div>
              <div class="kv-row">
                <span>OKX 连通性</span>
                <el-tag size="small" effect="plain" :type="system?.okxReachable ? 'success' : 'danger'">
                  {{ system?.okxReachable ? '可达' : '不可达' }}
                </el-tag>
              </div>
              <div class="kv-row">
                <span>示例数据回退</span>
                <el-tag size="small" effect="plain" :type="system?.syntheticFallbackEnabled ? 'warning' : 'info'">
                  {{ system?.syntheticFallbackEnabled ? '已启用' : '未启用' }}
                </el-tag>
              </div>
            </div>
          </div>
        </section>

        <section class="qts-card security-card">
          <div class="card-head">
            <div class="card-title">安全提示</div>
          </div>
          <div class="card-body">
            <ul class="security-list">
              <li>API Key 只需开启「读取」与「交易」权限，<b>切勿开启提现权限</b>。</li>
              <li>建议为 OKX API 设置 IP 白名单，并定期轮换密钥。</li>
              <li>完整密钥仅保存在后端数据库，前端不会回显已保存的密钥。</li>
              <li>实盘交易前请先用小额资金与模拟盘验证任务逻辑。</li>
              <li>回测与模拟盘结果不构成任何投资建议，请自行评估风险。</li>
            </ul>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Check, Connection, Refresh, RefreshLeft } from '@element-plus/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import { settingsApi } from '@/api'
import { fmtCompact, fmtDateTime, fmtMoney } from '@/utils/format'
import {
  DEFAULT_KLINE_COLORS,
  KLINE_COLOR_PRESETS,
  getKlineColors,
  matchKlinePreset,
  saveKlineColors
} from '@/utils/chartColors'

const okx = ref({})
const okxLoaded = ref(false)
const okxLoading = ref(false)
const ai = ref({})
const aiLoaded = ref(false)
const aiLoading = ref(false)
const aiSaving = ref(false)
const aiError = ref('')
const system = ref(null)
const systemLoading = ref(false)
const systemError = ref('')
const saving = ref(false)
const testing = ref(false)
const refreshing = ref(false)
const formError = ref('')
const testResult = ref(null)
const klinePresets = KLINE_COLOR_PRESETS
const klineColors = reactive(getKlineColors())
const activeKlinePreset = computed(() => matchKlinePreset(klineColors))

function applyKlinePreset(preset) {
  klineColors.up = preset.up
  klineColors.down = preset.down
  saveKlinePalette()
}

function saveKlinePalette() {
  const saved = saveKlineColors(klineColors)
  klineColors.up = saved.up
  klineColors.down = saved.down
  ElMessage.success('K 线配色已更新')
}

function resetKlinePalette() {
  klineColors.up = DEFAULT_KLINE_COLORS.up
  klineColors.down = DEFAULT_KLINE_COLORS.down
  saveKlinePalette()
}

const form = reactive({
  baseUrl: '',
  apiKey: '',
  secretKey: '',
  passphrase: '',
  simulatedTrading: true,
  liveTradingEnabled: false
})

const aiProviders = [
  {
    key: 'openai',
    label: 'OpenAI',
    baseUrl: 'https://api.openai.com/v1',
    model: 'gpt-4o-mini',
    models: ['gpt-4o-mini', 'gpt-4o', 'gpt-4.1-mini']
  },
  {
    key: 'deepseek',
    label: 'DeepSeek',
    baseUrl: 'https://api.deepseek.com/v1',
    model: 'deepseek-chat',
    models: ['deepseek-chat', 'deepseek-reasoner']
  },
  {
    key: 'moonshot',
    label: 'Moonshot Kimi',
    baseUrl: 'https://api.moonshot.cn/v1',
    model: 'moonshot-v1-8k',
    models: ['moonshot-v1-8k', 'moonshot-v1-32k']
  },
  {
    key: 'qwen',
    label: '通义千问',
    baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1',
    model: 'qwen-plus',
    models: ['qwen-plus', 'qwen-max', 'qwen-turbo']
  },
  {
    key: 'zhipu',
    label: '智谱 GLM',
    baseUrl: 'https://open.bigmodel.cn/api/paas/v4',
    model: 'glm-4-flash',
    models: ['glm-4-flash', 'glm-4-plus']
  },
  {
    key: 'custom',
    label: '自定义（OpenAI 兼容）',
    baseUrl: '',
    model: '',
    models: []
  }
]

const aiForm = reactive({
  enabled: false,
  providerType: 'openai',
  baseUrl: 'https://api.openai.com/v1',
  model: 'gpt-4o-mini',
  apiKey: '',
  timeoutSeconds: 30
})

const aiModelOptions = computed(
  () => aiProviders.find((provider) => provider.key === aiForm.providerType)?.models || []
)

function onAiProviderChange(key) {
  const provider = aiProviders.find((item) => item.key === key)
  if (!provider) return
  if (provider.baseUrl) aiForm.baseUrl = provider.baseUrl
  if (provider.model) aiForm.model = provider.model
}

function inferAiProvider(baseUrl) {
  const normalized = String(baseUrl || '').toLowerCase()
  const matched = aiProviders.find(
    (provider) => provider.baseUrl && normalized.startsWith(provider.baseUrl.toLowerCase())
  )
  return matched?.key || 'openai'
}

const DEFAULT_BASE_URL = 'https://www.okx.com'

function applyOkxData(data) {
  okx.value = data || {}
  form.baseUrl = data?.baseUrl || ''
  form.simulatedTrading = Boolean(data?.simulatedTrading ?? true)
  form.liveTradingEnabled = Boolean(data?.liveTradingEnabled ?? false)
  form.apiKey = ''
  form.secretKey = ''
  form.passphrase = ''
}

async function loadOkx() {
  okxLoading.value = true
  try {
    const res = await settingsApi.getOkx()
    applyOkxData(res)
    okxLoaded.value = true
    formError.value = ''
  } catch (err) {
    formError.value = err?.message || 'OKX 配置加载失败'
  } finally {
    okxLoading.value = false
  }
}

async function loadSystem() {
  systemLoading.value = true
  try {
    const res = await settingsApi.system()
    system.value = res || {}
    systemError.value = ''
  } catch (err) {
    systemError.value = err?.message || '系统状态加载失败'
  } finally {
    systemLoading.value = false
  }
}

function applyAiData(data) {
  ai.value = data || {}
  aiForm.enabled = Boolean(data?.enabled ?? false)
  aiForm.providerType = data?.providerType || inferAiProvider(data?.baseUrl)
  aiForm.baseUrl = data?.baseUrl || aiProviders.find((p) => p.key === aiForm.providerType)?.baseUrl || ''
  aiForm.model =
    data?.model || aiProviders.find((p) => p.key === aiForm.providerType)?.model || 'gpt-4o-mini'
  aiForm.timeoutSeconds = Number(data?.timeoutSeconds) || 30
  aiForm.apiKey = ''
}

async function loadAi() {
  aiLoading.value = true
  try {
    const res = await settingsApi.getAi()
    applyAiData(res)
    aiLoaded.value = true
    aiError.value = ''
  } catch (err) {
    aiError.value = err?.message || 'AI 配置加载失败'
  } finally {
    aiLoading.value = false
  }
}

async function saveAi() {
  aiError.value = ''
  if (!/^https?:\/\//i.test(aiForm.baseUrl.trim())) {
    aiError.value = 'Base URL 需以 http:// 或 https:// 开头'
    return
  }
  aiSaving.value = true
  try {
    const payload = {
      enabled: Boolean(aiForm.enabled),
      providerType: aiForm.providerType,
      baseUrl: aiForm.baseUrl.trim(),
      model: aiForm.model.trim(),
      timeoutSeconds: Number(aiForm.timeoutSeconds) || 30
    }
    if (aiForm.apiKey.trim()) payload.apiKey = aiForm.apiKey.trim()
    const res = await settingsApi.saveAi(payload)
    applyAiData(res)
    aiLoaded.value = true
    ElMessage.success('AI 配置已保存')
  } catch (err) {
    aiError.value = err?.message || 'AI 配置保存失败'
  } finally {
    aiSaving.value = false
  }
}

async function refreshAll() {
  refreshing.value = true
  try {
    await Promise.all([loadOkx(), loadAi(), loadSystem()])
  } finally {
    refreshing.value = false
  }
}

function effectiveCredentials() {
  return {
    apiKey: Boolean(form.apiKey.trim()) || Boolean(okx.value.hasApiKey),
    secretKey: Boolean(form.secretKey.trim()) || Boolean(okx.value.hasSecretKey),
    passphrase: Boolean(form.passphrase.trim()) || Boolean(okx.value.hasPassphrase)
  }
}

async function saveOkx() {
  formError.value = ''
  if (form.baseUrl.trim() && !/^https?:\/\//i.test(form.baseUrl.trim())) {
    formError.value = 'API 地址需以 http:// 或 https:// 开头'
    return
  }
  const normalizedBase = form.baseUrl.trim() === '' ? DEFAULT_BASE_URL : form.baseUrl.trim()
  const creds = effectiveCredentials()
  if ((form.apiKey.trim() || form.secretKey.trim() || form.passphrase.trim()) && !(creds.apiKey && creds.secretKey && creds.passphrase)) {
    formError.value = '请完整填写 API Key、Secret Key 与 Passphrase'
    return
  }
  saving.value = true
  try {
    const payload = {
      baseUrl: normalizedBase,
      simulatedTrading: Boolean(form.simulatedTrading),
      liveTradingEnabled: Boolean(form.liveTradingEnabled)
    }
    if (form.apiKey.trim()) payload.apiKey = form.apiKey.trim()
    if (form.secretKey.trim()) payload.secretKey = form.secretKey.trim()
    if (form.passphrase.trim()) payload.passphrase = form.passphrase.trim()
    const res = await settingsApi.saveOkx(payload)
    if (res && typeof res === 'object') {
      applyOkxData(res)
      okxLoaded.value = true
    } else {
      await loadOkx()
    }
    ElMessage.success('配置已保存')
  } catch (err) {
    formError.value = err?.message || '配置保存失败'
  } finally {
    saving.value = false
  }
}

async function testConnection() {
  testing.value = true
  testResult.value = null
  try {
    const res = await settingsApi.testOkx()
    testResult.value = res || { success: false, message: '未返回测试结果' }
    if (res?.success) ElMessage.success('OKX 连接成功')
    else ElMessage.error(res?.message || 'OKX 连接失败')
    await loadOkx()
  } catch (err) {
    testResult.value = { success: false, message: err?.message || '连接测试失败' }
    ElMessage.error(testResult.value.message)
  } finally {
    testing.value = false
  }
}

async function onLiveToggle(value) {
  if (!value) return
  try {
    await ElMessageBox.confirm(
      '开启后，Live 模式任务会直接使用真实资金下单。请确认你已完成模拟盘验证并了解相关风险。',
      '开启实盘交易',
      { type: 'warning', confirmButtonText: '确认开启', cancelButtonText: '取消' }
    )
  } catch {
    form.liveTradingEnabled = false
  }
}

onMounted(() => {
  loadOkx()
  loadAi()
  loadSystem()
})
</script>

<style scoped>
.settings-layout {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.settings-main,
.settings-side {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.settings-form :deep(.el-form-item) {
  margin-bottom: 16px;
}

.settings-form :deep(.el-form-item__label) {
  padding-bottom: 5px;
  font-size: 11.5px;
  color: var(--qts-text-muted);
}

.field-hint {
  margin-top: 5px;
  font-size: 10.5px;
  line-height: 1.55;
  color: var(--qts-text-dim);
}

.switch-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin: 4px 0 18px;
}

.switch-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 14px;
  border-radius: 11px;
  border: 1px solid rgba(148, 163, 184, 0.1);
  background: rgba(148, 163, 184, 0.03);
}

.switch-copy {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.switch-copy b {
  font-size: 12.5px;
  font-weight: 600;
  color: var(--qts-text);
}

.switch-copy span {
  font-size: 11px;
  color: var(--qts-text-dim);
  line-height: 1.5;
}

.settings-actions {
  display: flex;
  gap: 10px;
}

.settings-alert {
  margin: 10px 0 0;
}

.test-result {
  margin-top: 14px;
}

.okx-foot {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 24px;
  margin-top: 18px;
  padding-top: 14px;
  border-top: 1px solid rgba(148, 163, 184, 0.08);
  font-size: 11px;
  color: var(--qts-text-dim);
}

.okx-foot b {
  color: #cbd5e1;
  font-weight: 600;
}

.settings-desc {
  font-size: 12.5px;
  line-height: 1.75;
  color: var(--qts-text-muted);
}

.reset-row {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin: 14px 0 10px;
}

.settings-note {
  font-size: 11px;
  color: var(--qts-text-dim);
}

.kv-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.kv-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 12.5px;
  color: var(--qts-text-muted);
}

.kv-row b {
  color: var(--qts-text);
  font-weight: 600;
}

.security-list {
  margin: 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 9px;
  font-size: 12px;
  line-height: 1.7;
  color: var(--qts-text-muted);
}

.security-list b {
  color: #fda4af;
  font-weight: 600;
}

.security-list li::marker {
  color: rgba(34, 211, 238, 0.7);
}

.color-preset-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  margin: 14px 0 6px;
}

.color-preset {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-radius: 9px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background: rgba(10, 18, 33, 0.6);
  color: var(--qts-text-muted);
  cursor: pointer;
  transition: border-color 0.15s ease, background 0.15s ease;
}

.color-preset:hover,
.color-preset.active {
  border-color: rgba(34, 211, 238, 0.55);
  background: rgba(34, 211, 238, 0.08);
}

.color-swatch {
  width: 14px;
  height: 22px;
  border-radius: 3px;
  display: inline-block;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.1);
}

.color-preset-copy {
  display: flex;
  flex-direction: column;
  gap: 1px;
  text-align: left;
}

.color-preset-copy b {
  font-size: 12px;
  color: var(--qts-text);
  font-weight: 600;
}

.color-preset-copy small {
  font-size: 10.5px;
  color: var(--qts-text-dim);
}

.color-picker-row {
  margin-top: 8px;
}

.color-picker-line {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.color-code {
  font-family: 'JetBrains Mono', monospace;
  font-size: 11.5px;
  color: #cbd5e1;
}

@media (max-width: 1180px) {
  .settings-layout {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
