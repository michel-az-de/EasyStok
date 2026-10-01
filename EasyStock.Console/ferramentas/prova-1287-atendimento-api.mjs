/* eslint-disable no-console */
// Prova da issue #1287: pedido, cobrança, envio e sincronização do console no modo API.
// Um bloco por achado (1 a 10), com rede falsa e as funções puras de cada camada.
//
//   node ferramentas/prova-1287-atendimento-api.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // `fonteDados.js` lê `import.meta.env` do Vite; no node a fonte é fixa.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { deveRelerMensagens } = await import('../src/aplicacao/planoDeSincronizacao.js')
const { conversaDaApi, mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')
const conversasApi = await import('../src/infra/api/conversasApi.js')
const avisoSonoro = await import('../src/dominio/avisoSonoro.js').catch(() => ({}))

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const esperar = (ms = 20) => new Promise((r) => setTimeout(r, ms))
const JANELA = '22222222-2222-2222-2222-222222222222'
const ITEM = '11111111-1111-1111-1111-111111111111'
const PEDIDO_ID = 'a1b2c3d4-e5f6-4711-8899-aabbccddeeff'
const json = (data, status = 200) => new Response(JSON.stringify(status < 300 ? { data } : data), { status })

const pedidoApi = (cobranca = null) => ({
  pedidoId: PEDIDO_ID, status: 'aguardando_pagamento', itens: [{ cardapioItemId: ITEM, quantidade: 1 }],
  frete: 0, total: 50, cobranca,
})

// Rede falsa por rota: `rotas` é uma lista de [regex "METODO url", resposta | função].
function rede(rotas) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    const linha = `${method} ${url}`
    chamadas.push(linha)
    const rota = rotas.find(([re]) => re.test(linha))
    if (!rota) return json({ error: { code: 'X', message: 'sem rota na prova' } }, 500)
    const [, resposta] = rota
    return typeof resposta === 'function' ? resposta() : resposta.clone()
  }
  return chamadas
}

function montarComanda(pedido) {
  const despachados = []
  const estadoRef = { current: { conversas: [{ id: 'conv-1', pedido }] } }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef })
  const avisos = () => despachados.filter((a) => a.tipo === acao.AVISO_API).map((a) => a.mensagem)
  return { acoes, despachados, avisos }
}
const rascunho = (meio = 'cartao-link') => ({ pedidoId: null, janela: `${JANELA}|2026-10-02`, meio, itens: [{ sku: ITEM, qtd: 1, obs: '' }] })

// --- 1. Pedido sem link nunca recebe cobrança ----------------------------------
await confere('1a. pedido online criado sem cobrança avisa que o link não saiu', async () => {
  rede([
    [/^POST .*\/conversas\/conv-1\/pedido$/, json({ pedidoId: PEDIDO_ID, total: 50, forma: 'online', cobranca: null, enviadoAoCliente: true })],
    [/^GET .*\/conversas\/conv-1\/pedido$/, json(pedidoApi())],
  ])
  const { acoes, avisos } = montarComanda(rascunho())
  await acoes.gerarPedido('conv-1', 'cartao-link')
  assert.ok(avisos().some((m) => /link/i.test(m) && /cobran/i.test(m)), `sem aviso do link: ${avisos().join(' | ') || 'nenhum'}`)
})

await confere('1b. gerar cobrança com pedido criado e sem cobrança chama POST /api/pedidos/{id}/cobranca', async () => {
  const chamadas = rede([
    [/^POST .*\/api\/pedidos\/[^/]+\/cobranca$/, json({ cobranca: null })],
    [/^GET .*\/conversas\/conv-1\/pedido$/, json(pedidoApi())],
  ])
  const { acoes, avisos } = montarComanda({ ...rascunho(), pedidoId: PEDIDO_ID, cobranca: null })
  await acoes.gerarCobranca('conv-1', null, null)
  await esperar()
  assert.ok(chamadas.some((c) => c === `POST /api/pedidos/${PEDIDO_ID}/cobranca`), `sem POST da cobrança: ${chamadas.join(', ') || 'nenhuma'}`)
  assert.ok(!avisos().some((m) => /sozinho/.test(m)), 'ainda diz que o link sai sozinho')
})

await confere('1c. reenviar com cobrança existente continua sem chamar a reemissão', async () => {
  const chamadas = rede([])
  const { acoes } = montarComanda({ ...rascunho(), pedidoId: PEDIDO_ID, cobranca: { id: 'c1', meio: 'cartao-link' } })
  acoes.reenviarCobranca('conv-1', null, null, null)
  await esperar()
  assert.equal(chamadas.length, 0, `chamou a API: ${chamadas.join(', ')}`)
})

// --- 2. Duplo clique -------------------------------------------------------
await confere('2. segundo "Enviar ao cliente" com o primeiro em voo não faz outro POST', async () => {
  const pendentes = []
  const liberar = () => pendentes.forEach((r) => r(json({ pedidoId: PEDIDO_ID, cobranca: { cobrancaId: 'c' }, enviadoAoCliente: true })))
  const chamadas = rede([
    [/^POST .*\/conversas\/conv-1\/pedido$/, () => new Promise((r) => { pendentes.push(r) })],
    [/^GET .*\/conversas\/conv-1\/pedido$/, json(pedidoApi())],
  ])
  const { acoes } = montarComanda(rascunho())
  const primeira = acoes.gerarPedido('conv-1', 'cartao-link')
  const segunda = acoes.gerarCobranca('conv-1', null, 'cartao-link')
  await esperar()
  liberar()
  await primeira
  await segunda
  const posts = chamadas.filter((c) => /^POST .*\/pedido$/.test(c))
  assert.equal(posts.length, 1, `POSTs: ${posts.length}`)
  assert.ok(primeira && typeof primeira.then === 'function', 'gerarPedido não devolve a promessa (o botão não sabe quando destravar)')
})

// --- 3. GET de recarga falha depois do POST --------------------------------
await confere('3. recarga que falha depois do POST não diz "Pedido não criado"', async () => {
  rede([
    [/^POST .*\/conversas\/conv-1\/pedido$/, json({ pedidoId: PEDIDO_ID, cobranca: { cobrancaId: 'c' }, enviadoAoCliente: true })],
  ])
  const { acoes, avisos } = montarComanda(rascunho())
  await acoes.gerarPedido('conv-1', 'cartao-link')
  assert.ok(!avisos().some((m) => /não criado/.test(m)), `aviso falso: ${avisos().join(' | ')}`)
  assert.ok(avisos().some((m) => /criado/i.test(m) && /atualiz/i.test(m)), `sem aviso de atualização: ${avisos().join(' | ') || 'nenhum'}`)
})

// --- 4. Corrida envio x polling ---------------------------------------------
const msgServidor = { id: 'srv-1', dir: 'out', texto: 'oi', em: '2026-10-01T12:00:00Z', status: 'enviada' }
const conversaCom = (mensagens) => ({ id: 'conv-1', mensagens, pedido: null })
const estadoCom = (mensagens) => ({ conversas: [conversaCom(mensagens)], selecionadaId: 'conv-1', sincronizacao: {} })
const sincronizar = (estado, mensagens) => reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaCom(mensagens)] })
const confirmar = (estado) => reducer(estado, { tipo: acao.CONFIRMAR_ENVIO_API, id: 'conv-1', mensagemId: 'msg-1', mensagem: msgServidor })
const ids = (estado) => estado.conversas[0].mensagens.map((m) => m.id)

await confere('4a. sync traz a mensagem antes da confirmação: um balão só', () => {
  let estado = estadoCom([{ id: 'msg-1', dir: 'out', texto: 'oi', status: 'enviando' }])
  estado = sincronizar(estado, [msgServidor])
  estado = confirmar(estado)
  assert.deepEqual(ids(estado), ['srv-1'])
})

await confere('4b. confirmação antes de um sync que ainda não a tem: a mensagem fica', () => {
  let estado = estadoCom([{ id: 'msg-1', dir: 'out', texto: 'oi', status: 'enviando' }])
  estado = confirmar(estado)
  estado = sincronizar(estado, [])
  assert.deepEqual(ids(estado), ['srv-1'])
  estado = sincronizar(estado, [msgServidor])
  assert.deepEqual(ids(estado), ['srv-1'])
  estado = sincronizar(estado, [msgServidor])
  assert.deepEqual(ids(estado), ['srv-1'])
})

// --- 5 e 8. Envio pela API ---------------------------------------------------
function montarEnvio() {
  const despachados = []
  const chamadas = rede([[/^POST .*\/mensagens$/, json({ id: 'srv-9', direcao: 'Saida', status: 'Enviada', enviadaEm: '2026-10-01T12:00:00Z', tipoConteudo: 'Texto', texto: 'x' })]])
  const api = comApi({}, { despachar: (a) => despachados.push(a), agoraRef: { current: Date.now() }, estadoRef: { current: { conversas: [] } } })
  return { api, chamadas, despachados }
}

for (const [rotulo, opcoes] of [['modelo aprovado', { modelo: true }], ['mensagem automática', { automatica: true, regra: 'r1' }]]) {
  await confere(`5. ${rotulo} no modo API avisa e não vira texto livre`, async () => {
    const { api, chamadas, despachados } = montarEnvio()
    api.enviar('conv-1', 'Olá, tudo bem?', opcoes)
    await esperar()
    assert.equal(chamadas.length, 0, `enviou como texto: ${chamadas.join(', ')}`)
    assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /ainda não ligado/.test(a.mensagem)), 'não avisou')
    assert.ok(!despachados.some((a) => a.tipo === acao.ENVIAR_MENSAGEM), 'pôs balão na tela')
  })
}

await confere('8. texto vazio não é enviado', async () => {
  const { api, chamadas, despachados } = montarEnvio()
  api.enviar('conv-1', '   ')
  api.enviar('conv-1', '')
  await esperar()
  assert.equal(chamadas.length, 0, `chamou a API: ${chamadas.join(', ')}`)
  assert.equal(despachados.length, 0, 'despachou algo')
})

await confere('8b. texto normal continua saindo', async () => {
  const { api, chamadas } = montarEnvio()
  api.enviar('conv-1', 'oi')
  await esperar()
  assert.equal(chamadas.length, 1)
})

// --- 6. Som no primeiro sync ---------------------------------------------------
await confere('6. primeiro retrato com conversas depois da lista vazia não toca nada', () => {
  assert.equal(typeof avisoSonoro.mudancasDoRetrato, 'function', 'mudancasDoRetrato não existe')
  const retrato = new Map([['c1', { pago: false, precisa: false, ultimaMensagemClienteId: 'm1' }]])
  assert.deepEqual(avisoSonoro.mudancasDoRetrato(new Map(), retrato), [])
  assert.deepEqual(avisoSonoro.mudancasDoRetrato(null, retrato), [])
})

await confere('6b. mensagem nova de verdade continua avisando', () => {
  const antes = new Map([['c1', { pago: false, precisa: false, ultimaMensagemClienteId: 'm1' }]])
  const depois = new Map([['c1', { pago: false, precisa: false, ultimaMensagemClienteId: 'm2' }]])
  const mudancas = avisoSonoro.mudancasDoRetrato(antes, depois)
  assert.equal(mudancas.length, 1)
  assert.equal(mudancas[0].mensagemNova, true)
})

// --- 7. Número do pedido -------------------------------------------------------
await confere('7. número do pedido é o código curto do backend (8 hex, maiúsculo)', () => {
  assert.equal(pedidoDaApi(pedidoApi()).numero, 'A1B2C3D4')
})

// --- 9. Carga inicial ------------------------------------------------------------
await confere('9a. carga inicial não lê mensagens de conversa encerrada não selecionada', () => {
  const encerrada = { id: 'c-enc', situacao: 'Encerrada', ultimaMensagemEm: '2026-09-01T10:00:00' }
  assert.equal(deveRelerMensagens(encerrada, undefined, 'outra'), false)
  assert.equal(deveRelerMensagens(encerrada, undefined, 'c-enc'), true)
  assert.equal(deveRelerMensagens({ ...encerrada, situacao: 'Assumida' }, undefined, 'outra'), true)
})

await confere('9b. cartão sem mensagens carregadas usa ultimaMensagemTexto do resumo', () => {
  const resumo = { id: 'c-enc', situacao: 'Encerrada', canal: 'WhatsApp', contatoIdExterno: '5511', ultimaMensagemEm: '2026-09-01T10:00:00', ultimaMensagemTexto: 'Obrigada!', naoLidas: 0 }
  assert.equal(previaDaConversa(conversaDaApi(resumo, [], null)).corpo, 'Obrigada!')
})

// --- 10. Mídia recebida ------------------------------------------------------------
await confere('10a. mensagem com mídia traz o que o balão precisa para buscar o arquivo', () => {
  const m = mensagemDaApi({ id: 'm-foto', direcao: 'Entrada', tipoConteudo: 'Imagem', midiaChave: 'k', midiaMime: 'image/jpeg', enviadaEm: '2026-10-01T12:00:00Z' }, 'conv-1')
  assert.deepEqual(m.midia, { conversaId: 'conv-1', mensagemId: 'm-foto', mime: 'image/jpeg' })
})

await confere('10b. baixarMidia busca o arquivo autenticado como blob', async () => {
  assert.equal(typeof conversasApi.baixarMidia, 'function', 'baixarMidia não existe')
  const chamadas = rede([[/^GET .*\/conversas\/conv-1\/mensagens\/m-foto\/midia$/, new Response(new Blob(['abc'], { type: 'image/jpeg' }), { status: 200 })]])
  const blob = await conversasApi.baixarMidia('conv-1', 'm-foto')
  assert.equal(chamadas.length, 1)
  assert.equal(await blob.text(), 'abc')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
