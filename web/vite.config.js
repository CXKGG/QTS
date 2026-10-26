import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// QTS 前端构建配置
// - 开发端口 5173，/api 代理到后端 http://localhost:5088
// - 生产构建输出到后端静态目录 QTS/wwwroot，由 ASP.NET Core 直接托管
// - base 使用相对路径，保证产物放在任意根路径下都能加载 assets
export default defineConfig({
  plugins: [vue()],
  base: './',
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5088',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: '../QTS/wwwroot',
    emptyOutDir: true,
    assetsDir: 'assets',
    sourcemap: false,
    chunkSizeWarningLimit: 1800,
    rollupOptions: {
      output: {
        manualChunks: {
          echarts: ['echarts']
        }
      }
    }
  }
})
