// Regressões #1426: persistência, retentativa sem duplicar e push limitado à sessão.
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { runInNewContext } from 'node:vm'
registerHooks({
  resolve(s, c, next) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* padrão */ } }
    return next(s, c)
  },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return next(url, c)
  },
})
const EMPRESA = '11111111-1111-1111-1111-111111111111'
const CONVERSA = '22222222-2222-2222-2222-222222222222'
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'tk', expiraEm: Date.now() + 600000, empresa: { id: EMPRESA } }) }
const chamadas = []
let responder = () => [200, { data: null }]
globalThis.fetch = async (url, opts = {}) => {
  chamadas.push({ url, metodo: opts.method, corpo: opts.body && JSON.parse(opts.body), headers: opts.headers })
  const [status, data] = await responder(url, opts)
  return new Response(data === null ? null : JSON.stringify(data), { status })
}
const api = await import('../src/infra/api/notificacoesApi.js')
const { lembreteDaApi } = await import('../src/infra/api/traducaoNotificacoes.js')
const { criarAcoesLembretesApi, lerLembretesDaApi } = await import('../src/aplicacao/api/lembretes.js')
const acao = await import('../src/aplicacao/acoes.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { chaveLembrete, ORIGENS } = await import('../src/dominio/lembrete.js')
const { ativarAvisosNoAparelho, conferirAvisosNoAparelho } = await import('../src/aplicacao/avisosNoAparelho.js')
const push = await import('../src/infra/pushNavegador.js')
const dto = { id: 'l1', tipo: 'Manual', texto: 'Ligar', venceEm: '2026-10-09T12:00:00', conversaId: CONVERSA }
const l = lembreteDaApi(dto)
assert.equal(l.quando, Date.parse('2026-10-09T12:00:00Z'))
await api.listarLembretes()
await api.marcarLembretesVistosNaApi()
await api.concluirLembreteNaApi('l1')
await api.inscreverPush({ endpoint: 'https://push/x', p256dh: 'p', auth: 'a' })
await api.obterChavePush()
assert.deepEqual(chamadas.map(c => `${c.metodo} ${c.url}`), [
  'GET /api/atendimento/lembretes', 'POST /api/atendimento/lembretes/vistos',
  'POST /api/atendimento/lembretes/l1/concluir', 'POST /api/pwa/push/subscribe', 'GET /api/pwa/push/vapid-public',
])
assert.equal(chamadas[4].headers.Authorization, undefined)
assert.equal(chamadas[0].headers.Authorization, 'Bearer tk')
responder = () => [200, { data: [dto, { ...dto, id: 'auto', tipo: 'ClienteSemResposta' }] }]
assert.deepEqual(await lerLembretesDaApi(), [l, lembreteDaApi({ ...dto, id: 'auto', tipo: 'ClienteSemResposta' })], 'o avaliador fornece também os automáticos, sem consultar InApp')
responder = () => [503, { error: { message: 'Indisponível' } }]
await assert.rejects(lerLembretesDaApi(), /Indisponível/)

// Clique duplo e resposta perdida: um POST em voo, mesma chave na retentativa.
const despachos = []
const acoes = criarAcoesLembretesApi({ despachar: d => despachos.push(d) })
chamadas.length = 0
responder = () => { throw new Error('resposta perdida') }
const primeira = acoes.criarLembrete(l)
assert.equal(acoes.criarLembrete(l), primeira)
await assert.rejects(primeira, /Sem conexão/)
assert.equal(chamadas.length, 1)
assert.equal(despachos.length, 0)
const chave = chamadas[0].headers['Idempotency-Key']
assert.ok(chave)
responder = () => [200, { data: dto }]
await acoes.criarLembrete(l)
assert.equal(chamadas[1].headers['Idempotency-Key'], chave)
assert.deepEqual(chamadas[1].corpo, { texto: 'Ligar', venceEm: '2026-10-09T12:00:00.000Z', conversaId: CONVERSA })
assert.equal(despachos[0].lembrete.id, l.id)
await acoes.criarLembrete(l)
assert.notEqual(chamadas[2].headers['Idempotency-Key'], chave, 'nova criação confirmada é outra operação')
responder = () => [503, { error: { message: 'Falha ao gravar' } }]
despachos.length = 0
await assert.rejects(acoes.concluirLembrete(l), /Falha ao gravar/)
await assert.rejects(acoes.marcarLembretesVistos([chaveLembrete(l)]), /Falha ao gravar/)
assert.equal(despachos.length, 0, 'falha não conclui nem marca visto só na tela')
responder = () => [200, { data: dto }]
await acoes.concluirLembrete(l)
assert.equal(despachos[0].conversaId, null, 'não inventa mensagem de sistema')

// Consulta antiga não desfaz uma gravação; visto vem do servidor após recarregar.
const automatico = { id: 'auto', origem: ORIGENS.PASSAGEM, quando: 1 }
let estado = { lembretes: [automatico], vistos: {}, lembretesConcluidos: {}, conversas: [] }
estado = reducer(estado, { tipo: acao.CRIAR_LEMBRETE, lembrete: l })
assert.equal(reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: [], revisao: 0 }), estado)
estado = reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: [{ ...l, visto: true }], revisao: 1 })
assert.equal(estado.vistos[chaveLembrete(l)], true)
assert.equal(estado.lembretes.length, 2)
estado = reducer(estado, { tipo: acao.CONCLUIR_LEMBRETE, lembreteId: l.id })
estado = reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: [l] })
assert.deepEqual(estado.lembretes, [automatico], 'uma consulta atrasada não ressuscita concluído')

const inscricao = { endpoint: 'https://push/x', p256dh: 'p', auth: 'a' }
function contexto({ suportado = true, permissao = 'default', resposta = 'granted', existente = null, erroChave, erroGravar } = {}) {
  const passos = []
  const c = { passos, identidade: 'empresa:pessoa', atual: () => true }
  c.navegador = {
    suportado: () => suportado, permissao: () => permissao,
    pedirPermissao: () => { passos.push('permissao'); return Promise.resolve(resposta) },
    inscricaoAtual: async () => { passos.push('atual'); return existente },
    inscrever: async () => { passos.push('inscrever'); return inscricao },
    associar: () => passos.push('associar'), desinscrever: async () => passos.push('desligar'),
  }
  c.api = {
    obterChavePush: async () => { passos.push('chave'); if (erroChave) throw erroChave; return { publicKey: 'BAA' } },
    inscreverPush: async () => { passos.push('gravar'); if (erroGravar) throw new Error('Falha no servidor') },
  }
  return c
}
const ok = contexto()
const ativacao = ativarAvisosNoAparelho(ok)
assert.deepEqual(ok.passos, ['permissao'], 'permissão dentro do gesto, antes de qualquer espera')
assert.equal((await ativacao).estado, 'ativo')
assert.deepEqual(ok.passos, ['permissao', 'chave', 'inscrever', 'gravar', 'associar'])
for (const [opcoes, esperado] of [
  [{ suportado: false }, 'indisponivel'], [{ resposta: 'denied' }, 'bloqueado'],
  [{ resposta: 'default' }, 'desligado'], [{ erroChave: { status: 404 } }, 'sem-chave'], [{ erroGravar: true }, 'erro'],
]) assert.equal((await ativarAvisosNoAparelho(contexto(opcoes))).estado, esperado)
const abrir = contexto({ permissao: 'granted', existente: inscricao })
assert.equal((await conferirAvisosNoAparelho(abrir)).estado, 'ativo')
assert.deepEqual(abrir.passos, ['atual', 'gravar', 'associar'], 'consulta não pede permissão')
const semDono = contexto({ permissao: 'granted' })
assert.equal((await conferirAvisosNoAparelho(semDono)).estado, 'desligado')
assert.deepEqual(semDono.passos, ['atual'], 'não assume inscrição de outra conta')
const trocou = contexto()
let atual = true
trocou.atual = () => atual
trocou.api.inscreverPush = async () => { atual = false }
assert.equal((await ativarAvisosNoAparelho(trocou)).estado, 'erro')
assert.equal(trocou.passos.at(-1), 'desligar', 'sair durante a gravação invalida a inscrição tardia')
assert.ok(!trocou.passos.includes('associar'))

// Adaptador real com navegador controlado: rotação de conta, desligamento, outro SW.
const armazenamento = new Map()
globalThis.localStorage = { getItem: k => armazenamento.get(k) ?? null, setItem: (k, v) => armazenamento.set(k, v), removeItem: k => armazenamento.delete(k) }
globalThis.window = { location: { href: 'https://console.example.test/#/' }, PushManager: {}, Notification: {}, isSecureContext: true }
let fechados = 0, desinscricoes = 0, criacoes = 0
let assinatura = null
const registro = {
  active: { scriptURL: 'https://console.example.test/sw-avisos.js' },
  getNotifications: async () => [{ close: () => { fechados++ } }],
  pushManager: {
    getSubscription: async () => assinatura,
    subscribe: async ({ applicationServerKey }) => {
      criacoes++
      assinatura = { endpoint: `https://push/${criacoes}`, options: { applicationServerKey },
        toJSON: () => ({ endpoint: assinatura.endpoint, keys: { p256dh: 'p', auth: 'a' } }),
        unsubscribe: async () => { desinscricoes++; assinatura = null; return true },
      }
      return assinatura
    },
  },
}
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { userAgent: 'teste', serviceWorker: {
  getRegistration: async () => registro, register: async () => registro, ready: Promise.resolve(registro),
} } })
const primeiraInscricao = await push.inscreverPushNoNavegador('BAA', 'casa:ana')
push.associarInscricaoPush('casa:ana', primeiraInscricao.endpoint)
assert.equal((await push.inscricaoPushAtual('casa:ana')).endpoint, primeiraInscricao.endpoint)
assert.equal(await push.inscricaoPushAtual('casa:bia'), null)
await push.prepararPushParaSessao('casa:bia')
assert.equal(desinscricoes, 1)
assert.equal(fechados, 1)
const segundaInscricao = await push.inscreverPushNoNavegador('BAA', 'casa:bia')
push.associarInscricaoPush('casa:bia', segundaInscricao.endpoint)
assert.notEqual(segundaInscricao.endpoint, primeiraInscricao.endpoint)
await push.desinscreverPushNoNavegador('casa:ana')
assert.equal(desinscricoes, 1, 'saída antiga não desliga a conta nova')
await push.desinscreverPushNoNavegador(null, primeiraInscricao.endpoint)
assert.equal(desinscricoes, 1, 'conclusão tardia só invalida seu endpoint')
await push.desinscreverPushNoNavegador('casa:bia')
assert.equal(desinscricoes, 2)
registro.active.scriptURL = 'https://console.example.test/sw-pwa.js'
await assert.rejects(push.inscreverPushNoNavegador('BAA', 'casa:ana'), /Outro aplicativo/)
assert.equal(criacoes, 2)

// O SW apresenta a carga e foca o Console sem interceptar rede ou navegar para a carga.
const eventos = {}, exibidos = []
let focos = 0
const sw = { registration: { scope: 'https://console.example.test/', showNotification: async (titulo, dados) => exibidos.push({ titulo, dados }) },
  addEventListener: (evento, handler) => { eventos[evento] = handler },
  clients: { matchAll: async () => [{ url: 'https://console.example.test/#/', focus: async () => { focos++ } }] },
}
runInNewContext(readFileSync(new URL('../public/sw-avisos.js', import.meta.url), 'utf8'), { self: sw })
let esperando
eventos.push({ data: { json: () => ({ title: 'Lembrete', body: 'Ligar', tag: 'id' }) }, waitUntil: p => { esperando = p } })
await esperando
assert.equal(exibidos[0].titulo, 'Lembrete')
assert.equal(exibidos[0].dados.body, 'Ligar')
eventos.notificationclick({ notification: { close() {} }, waitUntil: p => { esperando = p } })
await esperando
assert.equal(focos, 1)
assert.equal(eventos.fetch, undefined)
console.log('OK #1426: lembretes persistidos, retentativa idempotente, leitura, push por conta e service worker.')
