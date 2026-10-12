/* eslint-disable no-console */
// Prova da #1548 (banca do console): a parte que roda sem React.
//   1. leitura do pedido que saiu antes não desfaz a que saiu depois, chegue pela polling
//      (SINCRONIZAR_CONVERSAS) ou pela recarga de uma ação (SINCRONIZAR_PEDIDO);
//   2. produção do dia: corpo corrigido depois de resposta perdida ganha Idempotency-Key nova
//      (a API recusa a mesma chave com outro corpo); o mesmo corpo segue com a mesma chave.
//
//   node ferramentas/prova-1548-banca-console.mjs

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
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { obterPedido, pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarProducaoDoDia } = await import('../src/aplicacao/producaoDoDia.js')
const { VALIDADE_DA_CHAVE_MS } = await import('../src/aplicacao/chaveDeEnvio.js')

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

// --- 1. Ordem das leituras do pedido -----------------------------------------------------
const PEDIDO = '11111111-2222-3333-4444-555555555555'
const doServidor = (status) => ({ pedidoId: PEDIDO, status, itens: [], frete: 0, total: 93, cobranca: null })
const resposta = (status) => new Response(JSON.stringify({ data: doServidor(status) }), { status: 200 })

// Duas leituras em voo: a primeira (polling) sai antes do pagamento, a segunda (recarga da ação)
// sai depois. Devolve as duas já traduzidas, na ordem em que SAÍRAM.
async function leiturasCruzadas() {
  const pendentes = []
  globalThis.fetch = () => new Promise((resolver) => { pendentes.push(resolver) })
  const daPolling = obterPedido('conv-1')
  const daAcao = obterPedido('conv-1')
  // `chamarApi` espera a sessão antes do fetch: dá a vez até as duas requisições saírem.
  for (let i = 0; i < 50 && pendentes.length < 2; i += 1) await new Promise((r) => setTimeout(r, 0))
  assert.equal(pendentes.length, 2, 'as duas leituras saíram')
  // A resposta mais nova chega primeiro; a antiga chega atrasada.
  pendentes[1](resposta('pago'))
  const nova = pedidoDaApi(await daAcao)
  pendentes[0](resposta('aguardando_pagamento'))
  const antiga = pedidoDaApi(await daPolling)
  return { antiga, nova }
}

const conversa = (pedido) => ({
  id: 'conv-1', nome: 'Maria', conta: 'cliente', estado: 'Aberto', mensagens: [], cliente: {}, pedido,
})
const base = { ...estadoInicial, conversas: [conversa(null)], selecionadaId: 'conv-1' }
const pedidoDe = (estado) => estado.conversas[0].pedido
const daPolling = (estado, pedido) => reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversa(pedido)] })
const daAcao = (estado, pedido) => reducer(estado, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'conv-1', pedido })

const { antiga, nova } = await leiturasCruzadas()

await confere('cada leitura do pedido leva a ordem em que saiu', () => {
  assert.equal(antiga.statusApi, 'aguardando_pagamento')
  assert.equal(nova.statusApi, 'pago')
  assert.ok(antiga.leitura < nova.leitura, `antiga ${antiga.leitura} < nova ${nova.leitura}`)
})

await confere('polling que saiu antes do pagamento não desfaz o pedido já atualizado', () => {
  const pago = daAcao(base, nova)
  assert.equal(pedidoDe(pago).statusApi, 'pago')
  assert.equal(pedidoDe(daPolling(pago, antiga)).statusApi, 'pago')
})

await confere('recarga de ação que saiu antes também não desfaz a mais nova', () => {
  const pago = daAcao(base, nova)
  assert.equal(pedidoDe(daAcao(pago, antiga)).statusApi, 'pago')
})

await confere('a leitura seguinte da polling volta a mandar', async () => {
  const pago = daAcao(base, nova)
  globalThis.fetch = async () => resposta('em_preparo')
  const seguinte = pedidoDaApi(await obterPedido('conv-1'))
  assert.ok(seguinte.leitura > nova.leitura)
  assert.equal(pedidoDe(daPolling(pago, seguinte)).statusApi, 'em_preparo')
})

await confere('na ordem certa, nada muda: a mais nova substitui a antiga', () => {
  const aguardando = daPolling(base, antiga)
  assert.equal(pedidoDe(aguardando).statusApi, 'aguardando_pagamento')
  assert.equal(pedidoDe(daAcao(aguardando, nova)).statusApi, 'pago')
})

await confere('pedido sem ordem de leitura (massa, provas antigas) segue substituindo', () => {
  const { leitura: _fora, ...semOrdem } = antiga
  assert.equal(pedidoDe(daPolling(daAcao(base, nova), semOrdem)).statusApi, 'aguardando_pagamento')
})

// --- 2. Produção do dia: chave por corpo -------------------------------------------------
const linha = (extra = {}) => ({ sku: 'i-1', porcoes: 2, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 5, ...extra })
const GRAVOU = { data: { loteId: 'l-1', codigoLote: 'LOT-261010-001', totalEtiquetas: 2, pratos: [] } }

// Guarda a chave de cada POST; `cair` derruba a rede depois de a requisição sair (resposta perdida).
function montarProducao(agora) {
  const chaves = []
  let cair = false
  let serie = 0
  globalThis.fetch = async (_url, { headers = {} } = {}) => {
    chaves.push(headers['Idempotency-Key'] ?? null)
    if (cair) throw new TypeError('Failed to fetch')
    return new Response(JSON.stringify(GRAVOU), { status: 201 })
  }
  const producao = criarProducaoDoDia({ gerarChave: () => `k-${serie += 1}`, agora })
  return { chaves, producao, rede: (fora) => { cair = fora } }
}

await confere('corpo corrigido depois de resposta perdida ganha chave nova', async () => {
  const { chaves, producao, rede } = montarProducao()
  rede(true)
  assert.equal((await producao.produzir([linha({ porcoes: 2 })])).ok, false)
  rede(false)
  assert.equal((await producao.produzir([linha({ porcoes: 3 })])).ok, true)
  assert.deepEqual(chaves, ['k-1', 'k-2'])
})

await confere('o mesmo corpo depois de resposta perdida repete a chave; gravou, chave nova', async () => {
  const { chaves, producao, rede } = montarProducao()
  rede(true)
  await producao.produzir([linha()])
  await producao.produzir([linha()])
  rede(false)
  await producao.produzir([linha()])
  await producao.produzir([linha()])
  assert.deepEqual(chaves, ['k-1', 'k-1', 'k-1', 'k-2'])
})

await confere('campo só da tela não troca a chave: o que conta é o corpo enviado', async () => {
  const { chaves, producao, rede } = montarProducao()
  rede(true)
  await producao.produzir([linha({ nome: 'Ravióli' })])
  await producao.produzir([linha({ nome: 'Ravióli de queijo' })])
  assert.deepEqual(chaves, ['k-1', 'k-1'])
})

await confere('a chave da produção vence como a do caixa', async () => {
  let relogio = 0
  const { chaves, producao, rede } = montarProducao(() => relogio)
  rede(true)
  await producao.produzir([linha()])
  relogio = VALIDADE_DA_CHAVE_MS
  await producao.produzir([linha()])
  relogio = 2 * VALIDADE_DA_CHAVE_MS + 1
  await producao.produzir([linha()])
  assert.deepEqual(chaves, ['k-1', 'k-1', 'k-2'])
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
