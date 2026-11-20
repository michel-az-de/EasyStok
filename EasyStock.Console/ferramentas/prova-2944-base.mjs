/* eslint-disable no-console */
// #2944: base das provas da E1 (hooks de módulo, `fonte`, `caso`, `fim`). Sem render de React no projeto: as provas leem
// o código por regex e rodam a lógica pura de verdade.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return next(url, c)
  },
})
globalThis.sessionStorage = { getItem: () => null, setItem() {}, removeItem() {} }

export { assert }
export const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
let passou = 0
const falhas = []
export const caso = async (nome, fn) => {
  try { await fn(); passou += 1; console.log(`ok    ${nome}`) } catch (e) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${String(e.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`) }
}
export const fim = () => {
  console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
  if (falhas.length) process.exit(1)
}
