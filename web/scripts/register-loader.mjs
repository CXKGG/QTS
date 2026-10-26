/**
 * 构建前置：检测当前环境是否允许创建子进程管道（esbuild 依赖）。
 * 若不允许（典型报错 spawn EPERM），注册 esbuild shim，并标记 QTS_ESBUILD_SHIM。
 */
import { spawnSync } from 'node:child_process'
import { register } from 'node:module'

function childPipesAvailable() {
  try {
    const result = spawnSync(process.execPath, ['-e', 'process.exit(0)'], {
      stdio: 'pipe',
      timeout: 15000,
      windowsHide: true
    })
    return result.status === 0
  } catch {
    return false
  }
}

if (!childPipesAvailable()) {
  process.env.QTS_ESBUILD_SHIM = '1'
  register(new URL('./esbuild-shim-loader.mjs', import.meta.url), import.meta.url)
  console.warn('[build] 当前环境不允许子进程管道，已启用 esbuild 兼容 shim。')
}
