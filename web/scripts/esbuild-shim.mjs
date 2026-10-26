/**
 * 受限环境兼容：esbuild 的 JS API 通过子进程（命名管道）通信。
 * 部分沙箱环境禁止创建管道，导致 spawn EPERM。此时用本 shim 替换 esbuild，
 * 仅实现 Vite 构建流程实际会用到的 define 替换能力。
 */

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

export async function transform(code, options = {}) {
  const define = options.define || {}
  let output = code
  const keys = Object.keys(define).sort((a, b) => b.length - a.length)
  for (const key of keys) {
    const value = define[key]
    const replacement = typeof value === 'string' ? value : JSON.stringify(value)
    output = output.replace(new RegExp(escapeRegExp(key), 'g'), () => replacement)
  }
  return {
    code: output,
    map: '',
    warnings: [],
    errors: []
  }
}

export async function build() {
  throw new Error('esbuild.build 在当前受限环境中不可用，已改用 configFile:false 构建')
}

export async function formatMessages(messages) {
  return Promise.resolve((messages || []).map((message) => message && (message.text || String(message))))
}

export async function context() {
  throw new Error('esbuild.context 在当前受限环境中不可用')
}

export const version = '0.0.0-qts-shim'

const shim = { transform, build, formatMessages, context, version }

export default shim
