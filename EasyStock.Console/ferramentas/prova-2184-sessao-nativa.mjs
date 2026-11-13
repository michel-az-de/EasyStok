import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { criarCanalSessaoNativa } from '../src/infra/sessaoNativa.js'

const mensagens = []
const ponte = { postMessage(texto) { mensagens.push(JSON.parse(texto)) }, onmessage: null }
const canal = criarCanalSessaoNativa(ponte, { tempoMaximo: 1000 })
const responder = (mensagem, dados) => ponte.onmessage({ data: JSON.stringify({ id: mensagem.id, dados }) })

const a = canal.obter()
const b = canal.obter()
assert.equal(mensagens.length, 1, 'duas telas compartilham uma restauração nativa')
assert.equal(mensagens[0].acao, 'obter')
responder(mensagens[0], { autenticado: true, token: 'jwt-curto', usuario: { id: 'pessoa-a' } })
assert.deepEqual(await a, await b)

const interrompida = canal.obter()
const recusada = assert.rejects(interrompida, erro => erro.codigo === 'SESSAO_ALTERADA')
const anterior = mensagens.at(-1)
const saida = canal.sair()
await recusada
assert.equal(mensagens.at(-1).acao, 'sair')
responder(anterior, { autenticado: true, token: 'resposta-atrasada' })
responder(mensagens.at(-1), { autenticado: false })
assert.equal((await saida).autenticado, false, 'resposta atrasada não desfaz Sair')

const nova = canal.obter()
responder(mensagens.at(-1), { autenticado: false })
assert.equal((await nova).autenticado, false)

const invalida = canal.obter()
const rejeitada = assert.rejects(invalida, erro => erro.status === 403 && erro.codigo === 'SEM_ACESSO')
ponte.onmessage({ data: JSON.stringify({ id: mensagens.at(-1).id, erro: { codigo: 'SEM_ACESSO', mensagem: 'Acesso revogado.', status: 403 } }) })
await rejeitada

const lentidao = canal.obter()
const expirou = assert.rejects(lentidao, erro => erro.codigo === 'NATIVO_NAO_RESPONDEU')
await expirou
const retomada = canal.obter()
responder(mensagens.at(-1), { autenticado: true, token: 'novo-jwt' })
assert.equal((await retomada).token, 'novo-jwt', 'falha temporária permite retentar')
canal.fechar()

// Exercita também o armazenamento real: nenhum refresh sai do proprietário Android.
const savedGlobals = { window: globalThis.window, localStorage: globalThis.localStorage, sessionStorage: globalThis.sessionStorage }
const memoryStorage = () => {
  const values = new Map()
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, String(value)), removeItem: key => values.delete(key) }
}
try {
  globalThis.window = new EventTarget()
  globalThis.localStorage = memoryStorage()
  globalThis.sessionStorage = memoryStorage()
  const source = readFileSync(new URL('../src/infra/api/sessao.js', import.meta.url), 'utf8')
    .replace("import { desinscreverPushNoNavegador } from '../pushNavegador'", 'const desinscreverPushNoNavegador = async () => {}')
  const store = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`)
  const claims = { sub: 'operadora-a', empresaId: 'empresa-a', dispositivoId: 'celular-a', sid: 'familia-a', sessaoNativa: 'true', exp: Math.floor(Date.now() / 1000) + 3600 }
  const jwt = value => `header.${Buffer.from(JSON.stringify(value)).toString('base64url')}.signature`
  const data = { autenticado: true, token: jwt(claims), empresaId: 'empresa-a', dispositivoId: 'celular-a', usuario: { id: 'operadora-a' } }
  store.gravarSessaoNativa(data)
  assert.equal(store.lerSessao().nativa, true)
  assert.equal(store.lerSessao().persistente, false)
  assert.equal(localStorage.getItem('easystok.sessao'), null)
  assert.equal('refreshToken' in JSON.parse(sessionStorage.getItem('easystok.sessao')), false)
  for (const wrong of [{ empresaId: 'empresa-b' }, { dispositivoId: 'celular-b' }, { usuario: { id: 'operadora-b' } }, { refreshToken: 'nao-deve-cruzar-a-ponte' }, { token: jwt({ ...claims, sessaoNativa: 'false' }) }]) {
    assert.throws(() => store.gravarSessaoNativa({ ...data, ...wrong }), error => error.codigo === 'SESSAO_NATIVA_INVALIDA')
    assert.equal(store.lerSessao().usuario.id, 'operadora-a')
  }
  assert.throws(() => store.gravarSessaoNativa({ ...data, token: jwt({ ...claims, exp: 1 }) }), error => error.codigo === 'SESSAO_NATIVA_VENCIDA')
  store.gravarSessaoNativa({ autenticado: false })
  assert.equal(store.lerSessao(), null)
} finally {
  Object.assign(globalThis, savedGlobals)
}
console.log('Sessão nativa: restauração compartilhada, Sair, resposta tardia, revogação e retomada aprovados.')
