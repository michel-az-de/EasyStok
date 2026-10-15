import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(spec, ctx, next) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return next(spec + '.js', ctx)
    return next(spec, ctx)
  },
  load(url, ctx, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const API_BASE = ''" }
    return next(url, ctx)
  },
})
const storage = () => {
  const dados = new Map()
  return { getItem: (k) => dados.get(k) ?? null, setItem: (k, v) => dados.set(k, v), removeItem: (k) => dados.delete(k),
    clear: () => dados.clear(), key: (i) => [...dados.keys()][i], get length() { return dados.size } }
}
globalThis.sessionStorage = storage()
globalThis.localStorage = storage()
globalThis.window = new EventTarget()
Object.defineProperty(globalThis, 'navigator', { value: { locks: { request: (_nome, acao) => acao() } }, configurable: true })
const s = await import('../src/infra/api/sessao.js')
const { chamarApi, garantirSessao } = await import('../src/infra/api/cliente.js')
const { entrar, entrarComGoogle, sair } = await import('../src/infra/api/autenticacao.js')
const chave = 'easystok.sessao'
let numero = 0
const jwt = (persistente = true, empresaId = 'empresa') => `cab.${Buffer.from(JSON.stringify({
  exp: Math.floor(Date.now() / 1000) + 3600, sub: 'pessoa', empresaId, sessaoPersistente: persistente ? 'true' : undefined, jti: ++numero,
})).toString('base64url')}.assinatura`
const dados = (persistente = true) => ({ token: jwt(persistente), refreshToken: `refresh-${numero}`, expiresIn: 3600,
  usuario: { id: 'pessoa', nome: 'Pessoa teste' } })
const resposta = (data, status = 200) => new Response(JSON.stringify({ data }), { status })
const abrir = (persistente = true, vencida = false) => {
  s.limparSessao()
  const atual = s.atualizarTokens({ usuario: { id: 'pessoa' }, empresa: { id: 'empresa' } }, dados(persistente))
  if (vencida) atual.expiraEm = Date.now() - 1000
  return s.gravarSessao(atual)
}

for (const login of [() => entrar('teste@local', 'senha-fixture'), () => entrarComGoogle('google-fixture')]) {
  for (const persistente of [false, true]) {
    globalThis.fetch = async () => resposta(dados(persistente))
    const atual = await login()
    assert.equal(atual.persistente, persistente)
    assert.equal(Boolean(localStorage.getItem(chave)), persistente)
    assert.equal(Boolean(sessionStorage.getItem(chave)), !persistente)
    sessionStorage.clear() // outra aba/visita
    assert.equal(Boolean(s.lerSessao()), persistente)
  }
}
// Uma propriedade local não promove Dona/Atendimento a sessão permanente.
abrir(false)
const falsa = { ...s.lerSessao(), persistente: true }
sessionStorage.clear()
localStorage.setItem(chave, JSON.stringify(falsa))
assert.equal(s.lerSessao(), null)

abrir(true, true)
let renovacoes = 0
let liberou
const espera = new Promise((resolve) => { liberou = resolve })
const tokenNovo = jwt()
globalThis.fetch = async (url, opts) => {
  if (url.endsWith('/refresh')) {
    renovacoes++
    await espera
    return resposta({ accessToken: tokenNovo, refreshToken: 'novo', expiresIn: 3600 })
  }
  assert.equal(opts.headers.Authorization, `Bearer ${tokenNovo}`)
  return resposta('ok')
}
const chamadas = [chamarApi('/fila'), chamarApi('/matriz'), garantirSessao()]
liberou()
await Promise.all(chamadas)
assert.equal(renovacoes, 1)
assert.equal(s.lerSessao().refreshToken, 'novo')

// Falha transitória preserva o refresh e nunca envia operação sem acesso válido.
for (const status of [0, 429, 500]) {
  abrir(true, true)
  globalThis.fetch = async (url) => {
    assert.ok(url.endsWith('/refresh'))
    if (!status) throw new Error('offline')
    return resposta(null, status)
  }
  await assert.rejects(chamarApi('/pedido', { metodo: 'POST', corpo: { teste: true } }))
  assert.ok(s.lerSessao()?.refreshToken)
}
for (const status of [401, 403]) {
  abrir(true, true)
  globalThis.fetch = async () => resposta(null, status)
  await assert.rejects(garantirSessao())
  assert.equal(s.lerSessao(), null)
}
// Revogação de capacidade persistente e troca de empresa vêm do token renovado.
abrir(true, true)
globalThis.fetch = async () => resposta({ accessToken: jwt(false), refreshToken: 'temporario', expiresIn: 3600 })
await garantirSessao()
assert.equal(s.lerSessao().persistente, false)
assert.equal(localStorage.getItem(chave), null)
abrir(true, true)
globalThis.fetch = async () => resposta({ accessToken: jwt(true, 'outra'), refreshToken: 'outra', expiresIn: 3600 })
await assert.rejects(garantirSessao())
assert.equal(s.lerSessao(), null)

// Sair enquanto renova limpa imediatamente e revoga também a chave recebida depois.
abrir(true, true)
let responderRefresh
const logouts = []
globalThis.fetch = async (url, opts) => {
  if (url.endsWith('/refresh')) return new Promise((resolve) => { responderRefresh = resolve })
  logouts.push(JSON.parse(opts.body).refreshToken)
  return resposta({ success: true })
}
const pendente = garantirSessao()
await sair()
assert.equal(s.lerSessao(), null)
responderRefresh(resposta({ accessToken: jwt(), refreshToken: 'atrasado', expiresIn: 3600 }))
await assert.rejects(pendente)
assert.ok(logouts.includes('atrasado'))
assert.equal(s.lerSessao(), null)

// Um 401 atrasado não derruba a sessão de uma pessoa que entrou depois.
abrir(false)
let devolver401
globalThis.fetch = async () => new Promise((resolve) => { devolver401 = resolve })
const antiga = chamarApi('/fila')
await Promise.resolve()
const nova = abrir(false)
devolver401(resposta(null, 401))
await assert.rejects(antiga)
assert.equal(s.lerSessao().token, nova.token)

// Navegador sem coordenação entre abas mantém somente sessão temporária.
navigator.locks = undefined
abrir(true)
assert.equal(s.lerSessao().persistente, false)
assert.equal(localStorage.getItem(chave), null)
console.log('Sessão: login senha/Google, permanência restrita, renovação única, offline, revogação, troca de empresa e saída concorrente aprovados.')
