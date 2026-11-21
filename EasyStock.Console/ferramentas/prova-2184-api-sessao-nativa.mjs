import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { registerHooks } from 'node:module'
import { test } from 'node:test'

// Sessão, cliente HTTP e canal nativo são módulos reais. Não há requisições externas.
registerHooks({
  resolve(spec, ctx, next) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return next(spec + '.js', ctx)
    return next(spec, ctx)
  },
  load(url, ctx, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const API_BASE = ''" }
    if (url.endsWith('/infra/pushNavegador.js')) return {
      format: 'module', shortCircuit: true, source: 'export const desinscreverPushNoNavegador = async () => {}',
    }
    if (process.env.EASYSTOK_2184_RED_DIR && /\/infra\/(?:api\/)?(?:sessao|cliente|sessaoNativa)\.js$/.test(url)) {
      return { format: 'module', shortCircuit: true,
        source: readFileSync(path.join(process.env.EASYSTOK_2184_RED_DIR, path.basename(url)), 'utf8') }
    }
    return next(url, ctx)
  },
})
const storage = () => {
  const data = new Map()
  return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) }
}
globalThis.sessionStorage = storage()
globalThis.localStorage = storage()
globalThis.window = new EventTarget()
Object.defineProperty(globalThis, 'navigator', { value: {}, configurable: true })
const s = await import('../src/infra/api/sessao.js')
const { chamarApi, garantirSessao } = await import('../src/infra/api/cliente.js')
const settle = () => new Promise(resolve => setImmediate(resolve))
const deferred = () => {
  let resolve
  const promise = new Promise(yes => { resolve = yes })
  return { promise, resolve }
}
let sequence = 0
function account(name = 'A', versao = 1) {
  const payload = { sub: `user-${name}`, empresaId: 'company-qa', dispositivoId: 'device-qa',
    sid: `session-${name}`, sessaoNativa: 'true', exp: Math.floor(Date.now() / 1000) + 3600, jti: ++sequence }
  return { autenticado: true, versao, sessaoId: payload.sid, usuario: { id: payload.sub, nome: `Pessoa QA ${name}` },
    empresaId: payload.empresaId, dispositivoId: payload.dispositivoId,
    token: `header.${Buffer.from(JSON.stringify(payload)).toString('base64url')}.fixture` }
}
function fixture(initial = account()) {
  s.limparSessao()
  const state = { current: initial, revalidate: null }, messages = [], operations = []
  const bridge = {
    onmessage: null,
    postMessage(raw) {
      const request = JSON.parse(raw); messages.push(request)
      assert.equal(request.acao, 'obter', 'um 401 revalida sem abrir Google nem executar logout genérico')
      const answer = request.tokenRejeitado && state.revalidate ? state.revalidate(request) : state.current
      Promise.resolve(answer).then(data => bridge.onmessage?.({ data: JSON.stringify({ id: request.id, dados: data }) }))
    },
  }
  window.EasyStokSession = bridge
  s.gravarSessaoNativa(initial)
  globalThis.fetch = async (url, options) => {
    operations.push({ url, options })
    return new Response('', { status: 401 })
  }
  return { state, messages, operations }
}

await test('401 comunica uma rejeição ao nativo e não repete o POST/mensagem', async () => {
  const initial = account(), f = fixture(initial)
  f.state.revalidate = () => (f.state.current = { autenticado: false, versao: 2 })
  await assert.rejects(chamarApi('/mensagens-qa', { metodo: 'POST', corpo: { texto: 'Mensagem QA' } }), error => error.status === 401)
  await settle()
  const rejected = f.messages.filter(message => message.tokenRejeitado)
  assert.equal(rejected.length, 1, 'a API deve avisar ao proprietário nativo sobre o JWT rejeitado')
  assert.equal(rejected[0].tokenRejeitado, initial.token)
  assert.equal(f.operations.length, 1, 'não reenviar uma mensagem cujo resultado falhou')
  assert.equal(await garantirSessao(), null, 'sessão revogada não reaparece na verificação seguinte')
})

await test('dois 401 simultâneos do mesmo JWT revalidam uma vez, sem repetir operações', async () => {
  const initial = account(), f = fixture(initial), pending = deferred()
  f.state.revalidate = () => pending.promise
  const calls = [chamarApi('/mensagens-qa', { metodo: 'POST', corpo: { texto: 'Uma' } }),
    chamarApi('/mensagens-qa', { metodo: 'POST', corpo: { texto: 'Duas' } })]
  const results = Promise.allSettled(calls)
  await settle()
  try {
    assert.equal(f.messages.filter(message => message.tokenRejeitado === initial.token).length, 1)
  } finally {
    f.state.current = account('A', 2); pending.resolve(f.state.current)
    await results
  }
  assert.equal(f.operations.length, 2, 'cada operação original foi tentada apenas uma vez')
  assert.ok((await results).every(result => result.status === 'rejected' && result.reason.status === 401))
  const restored = await garantirSessao()
  assert.equal(restored?.token, f.state.current.token)
  assert.equal(restored?.refreshToken, undefined, 'refresh continua exclusivamente nativo')
})

await test('401 tardio de A não encerra nem substitui a conta B', async () => {
  const initial = account(), f = fixture(initial), pending = deferred()
  globalThis.fetch = (url, options) => { f.operations.push({ url, options }); return pending.promise }
  const oldCall = chamarApi('/mensagens-qa', { metodo: 'POST', corpo: { texto: 'Da conta A' } })
  const rejected = assert.rejects(oldCall, error => error.status === 401)
  await settle()
  f.state.current = account('B', 3)
  s.gravarSessaoNativa(f.state.current)
  pending.resolve(new Response('', { status: 401 }))
  await rejected; await settle()
  assert.equal(s.lerSessao()?.usuario.id, 'user-B')
  assert.equal(s.lerSessao()?.token, f.state.current.token)
  assert.equal(f.operations.length, 1)
  assert.ok(f.messages.every(message => !message.tokenRejeitado || message.tokenRejeitado === initial.token))
  assert.equal(f.messages.some(message => message.acao === 'sair'), false)
})
