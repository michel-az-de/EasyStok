/* eslint-disable no-console */
// Prova da #1241 (F11, S45/S22): no modo API o item do cardápio e os alertas de saldo vêm do EasyStok.
//   - incluir, editar, tirar e repor gravam pelas rotas do Gerente; tirar esconde, não apaga;
//   - o cardápio lido traz os de fora (Repor); sem permissão, só os de dentro;
//   - os alertas de desacerto vêm de api/estoque/desacertos e o Entendi não os traz de volta.
//
//   node ferramentas/prova-f11-cardapio-itens.mjs

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
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { criarAcoesCardapioApi, lerCardapio, lerAlertasDeEstoque } = await import('../src/aplicacao/api/cardapio.js')

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

function montarFetch(rotas) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    for (const [teste, resposta] of rotas) {
      if (teste(method, url)) {
        const { status = 200, data = null, error } = resposta
        return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const DENTRO = [(m, u) => m === 'GET' && u.endsWith('/api/atendimento/comanda/cardapio'), { data: { itens: [
  { id: 'i-1', nome: 'Lasanha', linha: 'paraServir', precoCentavos: 4500, estoqueAtual: 3, disponivel: true },
] } }]
const FORA = [(m, u) => m === 'GET' && u.endsWith('/api/atendimento/comanda/cardapio/fora'), { data: [
  { cardapioItemId: 'i-9', nome: 'Nhoque', preco: 38, porcao: '500 g', categoria: 'Massas', fotoUrl: null },
] }]

function montar(rotas) {
  const chamadas = montarFetch([...rotas, FORA, DENTRO])
  const despachados = []
  const estadoRef = { current: { catalogo: { cardapio: [
    { sku: 'i-1', nome: 'Lasanha', linha: 'servir', porcao: '1 kg', preco: 45, estoque: 3, disponivelHoje: true, removidoEm: null },
    { sku: 'i-9', nome: 'Nhoque', linha: 'servir', porcao: '500 g', preco: 38, estoque: null, removidoEm: 'fora' },
  ] } } }
  const acoes = criarAcoesCardapioApi({ despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, chamadas, despachados }
}

await confere('item do cardápio saiu da lista de não ligadas (validar continua avisando)', () => {
  for (const nome of ['incluirItemCardapio', 'editarItemCardapio', 'alternarRemocaoItemCardapio']) {
    assert.equal(NAO_LIGADAS[nome], undefined, nome)
  }
  assert.ok(NAO_LIGADAS.confirmarValidacaoItem, 'sem campo no EasyStok, avisa')
})

await confere('lerCardapio junta os de dentro e os de fora, marcados como removidos', async () => {
  montarFetch([FORA, DENTRO])
  const cardapio = await lerCardapio()
  assert.deepEqual(cardapio.map((i) => [i.sku, Boolean(i.removidoEm)]), [['i-1', false], ['i-9', true]])
})

await confere('sem permissão para a lista de fora, lê só os de dentro', async () => {
  montarFetch([[(m, u) => u.endsWith('/cardapio/fora'), { status: 403 }], DENTRO])
  const cardapio = await lerCardapio()
  assert.deepEqual(cardapio.map((i) => i.sku), ['i-1'])
})

await confere('incluir manda nome, linha da API, porção e preço e relê', async () => {
  const { acoes, chamadas, despachados } = montar([
    [(m, u) => m === 'POST' && u.endsWith('/api/atendimento/comanda/cardapio'), { status: 201, data: { itemId: 'i-5' } }],
  ])
  const ok = await acoes.incluirItemCardapio({ nome: 'Torta', linha: 'casa', porcao: '6 fatias', preco: 60, adicionaisSelecionados: [] })
  assert.equal(ok, true)
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.deepEqual(post.corpo, { nome: 'Torta', linha: 'PrepararEmCasa', porcao: '6 fatias', preco: 60 })
  assert.ok(despachados.some((a) => a.tipo === acao.SINCRONIZAR_CARDAPIO))
  assert.ok(!despachados.some((a) => a.tipo === acao.INCLUIR_ITEM_CARDAPIO), 'não inventa sku local')
})

await confere('editar manda PUT do item', async () => {
  const { acoes, chamadas } = montar([[(m, u) => m === 'PUT' && u.endsWith('/comanda/cardapio/i-1'), { status: 204 }]])
  await acoes.editarItemCardapio('i-1', { nome: 'Lasanha bolonhesa', linha: 'servir', porcao: '1 kg', preco: 48 })
  const put = chamadas.find((c) => c.metodo === 'PUT')
  assert.deepEqual(put.corpo, { nome: 'Lasanha bolonhesa', linha: 'ParaServir', porcao: '1 kg', preco: 48 })
})

await confere('tirar esconde (visivel=false) e repor mostra (visivel=true)', async () => {
  const { acoes, chamadas } = montar([[(m, u) => m === 'POST' && u.includes('/visivel'), { data: {} }]])
  await acoes.alternarRemocaoItemCardapio('i-1', Date.now())
  await acoes.alternarRemocaoItemCardapio('i-9', Date.now())
  const posts = chamadas.filter((c) => c.metodo === 'POST')
  assert.deepEqual(posts.map((c) => [c.url.split('/').at(-2), c.corpo.visivel]), [['i-1', false], ['i-9', true]])
  assert.ok(!chamadas.some((c) => c.metodo === 'DELETE'), 'nunca apaga')
})

await confere('alertas de desacerto vêm da API', async () => {
  montarFetch([[(m, u) => u.includes('/api/estoque/desacertos'), { data: [
    { produtoId: 'p-1', nome: 'Lasanha', texto: 'Vendeu 2 sem saldo', quantidadeDescoberta: 2 },
  ] }]])
  const alertas = await lerAlertasDeEstoque()
  assert.deepEqual(alertas, [{ id: 'desacerto-p-1', texto: 'Vendeu 2 sem saldo', daApi: true }])
})

await confere('reducer troca os alertas da API e o Entendi não traz o mesmo de volta', () => {
  const inicial = { ...estadoInicial({ conversas: [] }), alertasEstoque: [{ id: 'local-1', texto: 'local' }] }
  const alerta = { id: 'desacerto-p-1', texto: 'Vendeu 2', daApi: true }
  const com = reducer(inicial, { tipo: acao.ALERTAS_DE_ESTOQUE_DA_API, alertas: [alerta] })
  assert.deepEqual(com.alertasEstoque.map((a) => a.id), ['local-1', 'desacerto-p-1'])
  const fechado = reducer(com, { tipo: acao.FECHAR_ALERTA, alertaId: 'desacerto-p-1' })
  const relido = reducer(fechado, { tipo: acao.ALERTAS_DE_ESTOQUE_DA_API, alertas: [alerta] })
  assert.deepEqual(relido.alertasEstoque.map((a) => a.id), ['local-1'])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
