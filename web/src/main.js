import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import './styles/main.css'

import App from './App.vue'
import router from './router'
import { pinia } from './stores'
import { useAuthStore } from './stores/auth'
import { setUnauthorizedHandler } from './api'

// 强制使用深色主题（Element Plus dark css vars）
document.documentElement.classList.add('dark')

const app = createApp(App)

// 全量注册 Element Plus 图标，模板中可直接 <component :is="'Odometer'" />
for (const [name, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(name, component)
}

app.use(pinia)
app.use(router)
app.use(ElementPlus, { locale: zhCn, size: 'default' })

// 401：清理本地会话 → 跳转登录页
setUnauthorizedHandler(() => {
  const auth = useAuthStore(pinia)
  auth.logout()
  const current = router.currentRoute.value
  if (current.path !== '/login') {
    const redirect = current.fullPath && current.fullPath !== '/' ? { redirect: current.fullPath } : {}
    router.replace({ path: '/login', query: redirect })
  }
})

app.mount('#app')
