import { pathToFileURL } from 'node:url'

const shimUrl = new URL('./esbuild-shim.mjs', import.meta.url).href

export async function resolve(specifier, context, nextResolve) {
  if (specifier === 'esbuild') {
    return { url: shimUrl, shortCircuit: true }
  }
  return nextResolve(specifier, context)
}
