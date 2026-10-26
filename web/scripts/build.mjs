/**
 * QTS Web 构建入口。
 *
 * 标准流程：使用 Vite 官方构建（esbuild 负责语法降级与压缩）。
 * 兼容流程（由 scripts/register-loader.mjs 检测并设置 QTS_ESBUILD_SHIM）：
 * 受限环境禁止 esbuild 子进程，改用 configFile:false + esbuild:false 的等价构建，
 * 由本地 define shim 完成 `process.env.NODE_ENV` 等替换。产物仍输出到 QTS/wwwroot，
 * 可正常由后端托管（受限环境下不做混淆压缩，仅影响产物体积）。
 */
import fs from 'node:fs'
import path from 'node:path'

const workspaceRoot = path.resolve(process.cwd())
const originalRealpathNative = fs.realpathSync.native
let realpathProbePatched = false
fs.realpathSync.native = function patchedRealpathNative(filePath, ...args) {
  if (!realpathProbePatched) {
    realpathProbePatched = true
    if (typeof filePath === 'string' && path.resolve(filePath) === workspaceRoot) {
      // 让 Vite 的 windowsSafeRealPathSync 走「非安全 realpath」分支，
      // 从而跳过 `exec('net use')`（在受限沙箱中会 spawn EPERM）。
      const error = new Error('EISDIR: illegal operation on a directory, realpath')
      error.code = 'EISDIR'
      throw error
    }
  }
  return originalRealpathNative.call(fs.realpathSync, filePath, ...args)
}

const shimmed = process.env.QTS_ESBUILD_SHIM === '1'
const { build } = await import('vite')

if (!shimmed) {
  await build()
} else {
  const { default: config } = await import('../vite.config.js')
  await build({
    ...config,
    configFile: false,
    esbuild: false,
    build: {
      ...config.build,
      // 受限环境下无法使用 esbuild 压缩，改用纯 JS 的 terser（进程内运行）
      minify: 'terser',
      cssMinify: false
    }
  })
}
