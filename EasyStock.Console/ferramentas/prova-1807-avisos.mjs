/* eslint-disable no-console */
// Prova da issue #1807 (revisão de 14/10/2026): "não estou recebendo notificação". O que o
// Balcão detecta entre dois retratos da lista vira aviso de tela (toast) e do navegador, além
// do som. Só regra pura e o canal do navegador com um Notification de mentira, sem tela.
//
//   A  primeira carga (sem retrato ou retrato vazio) não avisa nada;
//   B  os cinco eventos: contato novo, passou para você, mensagem nova, pagamento confirmado e
//      pedido que mudou de etapa, cada um com tipo, título e som próprios;
//   C  prioridades: passou para você cala a mensagem nova do mesmo retrato; o pedido que virou
//      pago avisa como pagamento, não como etapa; pedido que nasce não é "mudou de etapa";
//   D  a fila da tela: mesmo tipo e conversa substituem, limite de avisos visíveis;
//   E  o navegador: tag por tipo e conversa, só com permissão, e o uso antigo segue igual;
//   F  nenhum aviso carrega o texto da mensagem do cliente.
//
//   node ferramentas/prova-1807-avisos.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const aviso = await import('../src/dominio/aviso.js').catch((erro) => ({ erro }))
const { EVENTOS_DE_SOM } = await import('../src/dominio/automatico.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const linha = (extra = {}) => ({
  pago: false, precisa: false, ultimaMensagemClienteId: 'm1', etapa: null, nome: 'Marina Souza', motivo: null, ...extra,
})
const retrato = (pares) => new Map(pares)
const avisosDe = (antes, depois) => aviso.avisosDoRetrato(antes, depois)
const tipos = (lista) => lista.map((a) => a.tipo)

await confere('0. dominio/aviso.js existe com avisosDoRetrato e TIPOS_DE_AVISO', () => {
  assert.equal(aviso.erro, undefined, `import falhou: ${aviso.erro?.message}`)
  assert.equal(typeof aviso.avisosDoRetrato, 'function')
  assert.equal(typeof aviso.TIPOS_DE_AVISO, 'object')
})

const T = aviso.TIPOS_DE_AVISO ?? {}

// --- A. Primeira carga ----------------------------------------------------------
await confere('A1. sem retrato anterior não avisa, nem com pedido pago e precisa ligado', () => {
  const agora = retrato([['c1', linha({ pago: true, precisa: true, etapa: 'pago' })], ['c2', linha()]])
  assert.deepEqual(avisosDe(null, agora), [])
})

await confere('A2. retrato anterior vazio (modo API, lista nasce vazia) também não avisa', () => {
  const agora = retrato([['c1', linha({ etapa: 'preparo' })], ['c2', linha()], ['c3', linha()]])
  assert.deepEqual(avisosDe(new Map(), agora), [])
})

await confere('A3. retrato igual não avisa', () => {
  const r = retrato([['c1', linha({ etapa: 'preparo' })]])
  assert.deepEqual(avisosDe(r, retrato([['c1', linha({ etapa: 'preparo' })]])), [])
})

// --- B. Os cinco eventos ----------------------------------------------------------
const base = retrato([['c1', linha({ etapa: 'aguardando' })]])

await confere('B1. contato novo: conversa que não existia e chegou com fala do cliente', () => {
  const depois = retrato([...base, ['c2', linha({ nome: 'Rafael Lima', ultimaMensagemClienteId: 'x9' })]])
  const lista = avisosDe(base, depois)
  assert.deepEqual(tipos(lista), [T.CONTATO_NOVO])
  assert.equal(lista[0].conversaId, 'c2')
  assert.equal(lista[0].titulo, 'Contato novo')
  assert.match(lista[0].corpo, /Rafael/)
  assert.equal(lista[0].som, EVENTOS_DE_SOM.NOVO_ATENDIMENTO)
})

await confere('B1b. conversa nova sem fala do cliente (criada pela equipe) não avisa', () => {
  const depois = retrato([...base, ['c2', linha({ ultimaMensagemClienteId: null })]])
  assert.deepEqual(avisosDe(base, depois), [])
})

await confere('B2. passou para você: precisa acendeu, com o motivo no corpo', () => {
  const depois = retrato([['c1', linha({ etapa: 'aguardando', precisa: true, motivo: 'Agente passou para você' })]])
  const lista = avisosDe(base, depois)
  assert.deepEqual(tipos(lista), [T.PASSOU_PARA_VOCE])
  assert.equal(lista[0].titulo, 'Precisa de você')
  assert.equal(lista[0].corpo, 'Agente passou para você')
  assert.equal(lista[0].som, EVENTOS_DE_SOM.PASSOU_PARA_VOCE)
  assert.equal(lista[0].destaca, true)
})

await confere('B3. mensagem nova do cliente numa conversa que já existia', () => {
  const depois = retrato([['c1', linha({ etapa: 'aguardando', ultimaMensagemClienteId: 'm2' })]])
  const lista = avisosDe(base, depois)
  assert.deepEqual(tipos(lista), [T.MENSAGEM_NOVA])
  assert.equal(lista[0].titulo, 'Mensagem nova')
  assert.match(lista[0].corpo, /Marina/)
  assert.equal(lista[0].som, EVENTOS_DE_SOM.NOVO_ATENDIMENTO)
})

await confere('B4. pagamento confirmado', () => {
  const depois = retrato([['c1', linha({ etapa: 'pago', pago: true })]])
  const lista = avisosDe(base, depois)
  assert.deepEqual(tipos(lista), [T.PAGAMENTO_CONFIRMADO])
  assert.equal(lista[0].titulo, 'Pagamento confirmado')
  assert.equal(lista[0].som, EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO)
  assert.equal(lista[0].destaca, true)
})

await confere('B5. pedido mudou de etapa, com o rótulo da etapa nova e sem som', () => {
  const antes = retrato([['c1', linha({ etapa: 'pago', pago: true })]])
  const depois = retrato([['c1', linha({ etapa: 'entrega' })]])
  const lista = avisosDe(antes, depois)
  assert.deepEqual(tipos(lista), [T.ETAPA_DO_PEDIDO])
  assert.equal(lista[0].titulo, 'Pedido mudou de etapa')
  assert.match(lista[0].corpo, /Marina/)
  assert.match(lista[0].corpo, /entrega/i)
  assert.equal(lista[0].som, null)
})

await confere('B6. o mesmo retrato pode trazer eventos de conversas diferentes', () => {
  const antes = retrato([['c1', linha({ etapa: 'aguardando' })], ['c2', linha({ etapa: 'preparo' })]])
  const depois = retrato([
    ['c1', linha({ etapa: 'pago', pago: true })],
    ['c2', linha({ etapa: 'embalado' })],
    ['c3', linha({ ultimaMensagemClienteId: 'z1' })],
  ])
  assert.deepEqual(tipos(avisosDe(antes, depois)).sort(), [T.CONTATO_NOVO, T.ETAPA_DO_PEDIDO, T.PAGAMENTO_CONFIRMADO].sort())
})

// --- C. Prioridades -----------------------------------------------------------------
await confere('C1. passou para você e mensagem nova no mesmo retrato: só o passou (achado 6)', () => {
  const depois = retrato([['c1', linha({ etapa: 'aguardando', precisa: true, ultimaMensagemClienteId: 'm2' })]])
  assert.deepEqual(tipos(avisosDe(base, depois)), [T.PASSOU_PARA_VOCE])
})

await confere('C2. pedido que virou pago avisa como pagamento, não como etapa', () => {
  const depois = retrato([['c1', linha({ etapa: 'pago', pago: true })]])
  assert.ok(!tipos(avisosDe(base, depois)).includes(T.ETAPA_DO_PEDIDO))
})

await confere('C3. pedido que nasce (sem etapa antes) não é "mudou de etapa"', () => {
  const antes = retrato([['c1', linha({ etapa: null })]])
  const depois = retrato([['c1', linha({ etapa: 'aguardando' })]])
  assert.deepEqual(avisosDe(antes, depois), [])
})

await confere('C4. pedido que some da conversa não é "mudou de etapa"', () => {
  const antes = retrato([['c1', linha({ etapa: 'entregue' })]])
  const depois = retrato([['c1', linha({ etapa: null })]])
  assert.deepEqual(avisosDe(antes, depois), [])
})

// --- D. Fila da tela ----------------------------------------------------------------
await confere('D1. filaComAviso põe no fim, com id próprio', () => {
  assert.equal(typeof aviso.filaComAviso, 'function', 'filaComAviso não existe')
  const um = { tipo: T.MENSAGEM_NOVA, conversaId: 'c1', titulo: 'Mensagem nova', corpo: 'Marina mandou mensagem' }
  const fila = aviso.filaComAviso([], um, 'a1')
  assert.equal(fila.length, 1)
  assert.equal(fila[0].id, 'a1')
})

await confere('D2. mesmo tipo e mesma conversa substitui o aviso antigo em vez de empilhar', () => {
  const um = { tipo: T.MENSAGEM_NOVA, conversaId: 'c1', titulo: 'Mensagem nova', corpo: '1' }
  let fila = aviso.filaComAviso([], um, 'a1')
  fila = aviso.filaComAviso(fila, { ...um, corpo: '2' }, 'a2')
  assert.deepEqual(fila.map((a) => a.id), ['a2'])
  fila = aviso.filaComAviso(fila, { ...um, conversaId: 'c2' }, 'a3')
  assert.deepEqual(fila.map((a) => a.id), ['a2', 'a3'])
})

await confere('D3. a pilha mostra no máximo LIMITE_DE_AVISOS, o mais velho sai', () => {
  const limite = aviso.LIMITE_DE_AVISOS
  assert.ok(Number.isInteger(limite) && limite >= 3 && limite <= 5, `limite ${limite}`)
  let fila = []
  for (let i = 0; i < limite + 2; i += 1) {
    fila = aviso.filaComAviso(fila, { tipo: T.MENSAGEM_NOVA, conversaId: `c${i}`, titulo: 't', corpo: 'c' }, `a${i}`)
  }
  assert.equal(fila.length, limite)
  assert.equal(fila[0].id, 'a2')
})

// --- E. Canal do navegador ----------------------------------------------------------
const criadas = []
class NotificacaoFalsa {
  static permission = 'granted'
  constructor(titulo, opcoes) { criadas.push({ titulo, ...opcoes }) }
}
globalThis.window = { Notification: NotificacaoFalsa }
globalThis.Notification = NotificacaoFalsa
const navegador = await import('../src/infra/notificacaoNavegador.js')

await confere('E1. notificar(tipo, id, titulo, corpo) usa tag por tipo e conversa', () => {
  assert.equal(typeof navegador.notificar, 'function', 'notificar não existe')
  criadas.length = 0
  assert.equal(navegador.notificar(T.PAGAMENTO_CONFIRMADO, 'c1', 'Pagamento confirmado', 'Marina pagou'), true)
  assert.equal(navegador.notificar(T.ETAPA_DO_PEDIDO, 'c1', 'Pedido mudou de etapa', 'Marina: Em entrega'), true)
  assert.equal(criadas.length, 2)
  assert.notEqual(criadas[0].tag, criadas[1].tag)
  assert.match(criadas[0].tag, /c1/)
  assert.equal(criadas[0].body, 'Marina pagou')
})

await confere('E2. uso antigo (notificarPrecisaDeVoce) mantém a mesma tag de sempre', () => {
  criadas.length = 0
  navegador.notificarPrecisaDeVoce('c7', 'Precisa de você', 'motivo')
  assert.equal(criadas[0].tag, 'casa-da-baba-precisa-c7')
  navegador.notificar(T.PASSOU_PARA_VOCE, 'c7', 'Precisa de você', 'motivo')
  assert.equal(criadas[1].tag, 'casa-da-baba-precisa-c7')
})

await confere('E3. sem permissão não cria notificação', () => {
  criadas.length = 0
  NotificacaoFalsa.permission = 'default'
  assert.equal(navegador.notificar(T.CONTATO_NOVO, 'c1', 'Contato novo', 'x'), false)
  assert.equal(criadas.length, 0)
  NotificacaoFalsa.permission = 'granted'
})

// --- F. Sem o texto do cliente ----------------------------------------------------
await confere('F1. o aviso não leva o texto da mensagem, só nome e evento', () => {
  const antes = retrato([['c1', linha()]])
  const depois = retrato([['c1', { ...linha({ ultimaMensagemClienteId: 'm2' }), texto: 'meu cpf 123' }]])
  const [primeiro] = avisosDe(antes, depois)
  assert.ok(!JSON.stringify(primeiro).includes('cpf'))
})

await confere('G1. aba focada sem confirmação de toast não descarta o push', async () => {
  const eventos = new Map()
  let mostradas = 0, fechadas = 0, tarefa
  const self = {
    addEventListener: (tipo, fn) => eventos.set(tipo, fn),
    registration: {
      scope: 'https://app.exemplo/',
      showNotification: async () => { mostradas++ },
      getNotifications: async () => [{ close: () => { fechadas++ } }],
    },
    clients: { matchAll: async () => [{ url: 'https://app.exemplo/#/login', visibilityState: 'visible', focused: true }] },
  }
  runInNewContext(readFileSync(new URL('../public/sw-avisos.js', import.meta.url), 'utf8'), { self, URL })
  eventos.get('push')({ data: { json: () => ({ title: 'Contato novo', tag: 'contato-c1' }) }, waitUntil: (p) => { tarefa = p } })
  await tarefa
  assert.equal(mostradas, 1)
  assert.equal(fechadas, 0)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
