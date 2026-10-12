import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(id, context, next) {
  if (id.startsWith('.') && !/\.[a-z]+$/i.test(id)) return next(id + '.js', context)
  return next(id, context)
}, load(url, context, next) {
  if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
  return next(url, context)
} })
const { filtrarModulos, modulosDoHall } = await import('../src/dominio/modulos.js')
const hall = modulosDoHall({ fonteApi: true })
assert.equal(filtrarModulos(hall, '  ').length, 8)
assert.deepEqual(filtrarModulos(hall, 'PRODUCAO').map((m) => m.id), ['producao', 'cozinha'])
assert.deepEqual(filtrarModulos(hall, 'JANELAS ENTREGA').map((m) => m.id), ['entregas'])
assert.deepEqual(filtrarModulos(hall, 'xyz'), [])
assert.deepEqual(filtrarModulos(hall, 'caixa').map((m) => m.id), ['financeiro'])

const storage = new Map()
globalThis.window = new EventTarget()
globalThis.sessionStorage = { getItem: (k) => storage.get(k), setItem: (k, v) => storage.set(k, v), removeItem: (k) => storage.delete(k) }
const { entrar } = await import('../src/infra/api/autenticacao.js')
const empresaId = 'a1fd1e83-4d38-49b0-9000-390fa7390cfb'
const jwt = (empresa) => `test.${Buffer.from(JSON.stringify({ empresaId: empresa, exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url')}.test`
const chamadas = []
let token = jwt(empresaId)
globalThis.fetch = async (url, options) => {
  chamadas.push({ url, ...options })
  return { ok: true, text: async () => JSON.stringify({ data: { token, expiresIn: 3600, usuario: { nome: 'Operadora' } } }) }
}
const sessao = await entrar('operadora@example.test', 'senha-teste')
assert.equal(chamadas.length, 1)
assert.ok(chamadas[0].url.endsWith('/api/auth/login'))
assert.deepEqual(JSON.parse(chamadas[0].body), { email: 'operadora@example.test', senha: 'senha-teste', empresaId: null })
assert.equal(sessao.empresa.id, empresaId)
assert.equal(JSON.parse(storage.get('easystok.sessao')).token, token)
storage.clear()
token = jwt(null)
await assert.rejects(() => entrar('operadora@example.test', 'senha-teste'), /empresa ativa única/)
assert.equal(storage.size, 0, 'token sem empresa não inicia sessão')
console.log('Onda 1: busca por módulo/tela, login único, empresa do JWT e recusa de token sem empresa aprovados.')
