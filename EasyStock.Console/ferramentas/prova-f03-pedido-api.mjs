/* eslint-disable no-console */
// Prova da F03 (issue #1210): no modo API a comanda vira pedido e cobrança do EasyStok.
// Exercita a tradução do pedido da API para o formato do console (`infra/api/comandaApi.js`)
// e o merge da polling no reducer de verdade: rascunho local sobrevive enquanto o servidor
// não tem pedido; com pedido, o servidor manda e a janela escolhida fica.
//
//   node ferramentas/prova-f03-pedido-api.mjs

import { registerHooks } from 'node:module'
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
    return proximo(url, contexto)
  },
})

const {
  pedidoDaApi, corpoDoPedido, janelaDaApi, janelaDoId, produtoDaApi,
} = await import('../src/infra/api/comandaApi.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { situacaoDaCobranca, sinalVerde } = await import('../src/dominio/cobranca.js')
const { situacaoDoItem, baixarSaldo } = await import('../src/dominio/cardapio.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = Date.parse('2026-09-30T15:00:00Z')
confere('fotos persistidas chegam à galeria após sincronizar o cardápio', () => {
  const fotos = [1, 2, 3, 4].map((n) => `https://fotos.test/${n}.webp`)
  const produto = produtoDaApi({ id: 'foto-item', nome: 'Lasanha', precoCentavos: 5000, imagemUrl: fotos[0], fotos })
  const estado = reducer(estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] }), { tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: [produto] })
  assert.equal(produto.foto, fotos[0])
  assert.deepEqual(estado.catalogo.galeria.map((p) => p.foto), fotos)
  assert.ok(estado.catalogo.galeria.every((p) => p.doCardapio))
})
const LASANHA = '11111111-1111-1111-1111-111111111111'
const JANELA = '22222222-2222-2222-2222-222222222222'
const PEDIDO = 'abcdef12-3333-3333-3333-333333333333'

const doServidor = (status, cobranca) => ({
  pedidoId: PEDIDO,
  status,
  total: 93,
  frete: 8,
  criadoEm: '2026-09-30T14:50:00',
  agendadoParaEm: null,
  janela: { janelaId: JANELA, data: '2026-10-01', label: 'Almoço', horaInicio: '12:00:00', horaFim: '13:00:00' },
  itens: [{ cardapioItemId: LASANHA, nome: 'Lasanha', quantidade: 1, precoUnitario: 85, observacao: 'sem cebola' }],
  cobranca,
})
const cobrancaMp = (status, extra = {}) => ({
  cobrancaId: 'c1', provedor: 'mercadopago', status, linkPagamento: 'https://mp.test/1', valor: 93,
  expiraEm: '2026-09-30T15:20:00', pagaEm: null, valorPago: null, metodoPagamento: null, tentativa: 1,
  criadaEm: '2026-09-30T14:50:00', ...extra,
})

// --- Tradução ---------------------------------------------------------------
confere('item do cardápio vira produto com sku = cardapioItemId e preço em reais', () => {
  const produto = produtoDaApi({ id: LASANHA, nome: 'Lasanha', precoCentavos: 8500, linha: 'prepararEmCasa', pesoExibicao: '800 g', estoqueAtual: null, disponivel: true })
  assert.deepEqual([produto.sku, produto.preco, produto.linha, produto.estoque], [LASANHA, 85, 'casa', null])
})

confere('item sem controle de saldo fica disponível e não baixa para zero', () => {
  const produto = { sku: LASANHA, nome: 'Lasanha', estoque: null }
  assert.equal(situacaoDoItem(produto).chave, 'disponivel')
  assert.equal(baixarSaldo([produto], LASANHA)[0].estoque, null)
})

confere('janela da API carrega id e data no id do console', () => {
  const janela = janelaDaApi({ janelaId: JANELA, data: '2026-10-01', label: '12h às 13h', vagasRestantes: 3, capacidade: 5 })
  assert.deepEqual(janelaDoId(janela.id), { janelaId: JANELA, data: '2026-10-01' })
  assert.match(janela.rotulo, /qui 01\/10 · 12h às 13h/)
})

confere('comanda vira corpo do POST com janela, data e forma do meio', () => {
  const corpo = corpoDoPedido({
    janela: `${JANELA}|2026-10-01`, meio: 'maquininha', itens: [{ sku: LASANHA, qtd: 2, obs: '' }],
  })
  assert.deepEqual(corpo, {
    itens: [{ cardapioItemId: LASANHA, qtd: 2, observacao: null }],
    janelaId: JANELA, dataEntrega: '2026-10-01', forma: 'na_entrega',
  })
})

confere('cobrança pendente do Mercado Pago aguarda com o relógio da expiração', () => {
  const pedido = pedidoDaApi(doServidor('aguardando_pagamento', cobrancaMp('Pendente')))
  assert.equal(pedido.estado, 'aguardando')
  assert.equal(situacaoDaCobranca(pedido.cobranca, AGORA).chave, 'aguardando')
})

confere('cobrança vencida aparece expirada', () => {
  const pedido = pedidoDaApi(doServidor('aguardando_pagamento', cobrancaMp('Expirada', { expiraEm: '2026-09-30T14:40:00' })))
  assert.equal(situacaoDaCobranca(pedido.cobranca, AGORA).chave, 'expirada')
})

confere('pagamento confirmado pelo Mercado Pago vira pago e sinal verde', () => {
  const pedido = pedidoDaApi(doServidor('aguardando', cobrancaMp('Paga', { pagaEm: '2026-09-30T14:55:00', valorPago: 93, metodoPagamento: 'pix' })))
  assert.equal(pedido.estado, 'pago')
  assert.equal(situacaoDaCobranca(pedido.cobranca, AGORA).chave, 'conciliada')
  assert.equal(sinalVerde(pedido), true)
})

confere('na entrega fica combinada, sem relógio', () => {
  const pedido = pedidoDaApi(doServidor('aguardando_pagamento', {
    ...cobrancaMp('Pendente'), provedor: 'na_entrega', linkPagamento: null, expiraEm: null,
  }))
  assert.equal(pedido.meio, 'maquininha')
  assert.equal(situacaoDaCobranca(pedido.cobranca, AGORA).chave, 'combinada')
})

// --- Merge da polling no reducer --------------------------------------------
const conversa = (pedido) => ({
  id: 'conv-1', nome: 'Maria', conta: 'cliente', estado: 'Aberto', mensagens: [], cliente: {}, pedido,
})
const sincronizar = (estado, pedido) => reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversa(pedido)] })
const rascunho = { numero: '2026-0185', estado: 'aguardando', janela: `${JANELA}|2026-10-01`, itens: [{ sku: LASANHA, qtd: 1, obs: '' }], pagamentos: [], meio: 'pix' }
const base = { ...estadoInicial, conversas: [conversa(rascunho)], selecionadaId: 'conv-1' }

confere('servidor sem pedido não apaga a comanda que a dona monta', () => {
  const depois = sincronizar(base, null)
  assert.deepEqual(depois.conversas[0].pedido, rascunho)
})

confere('pedido criado no servidor substitui o rascunho e guarda a janela escolhida', () => {
  const depois = sincronizar(base, pedidoDaApi(doServidor('aguardando_pagamento', cobrancaMp('Pendente'))))
  const pedido = depois.conversas[0].pedido
  assert.equal(pedido.pedidoId, PEDIDO)
  assert.equal(pedido.janela, rascunho.janela)
  assert.equal(pedido.cobranca.link, 'https://mp.test/1')
})

confere('pedido antigo entregue não apaga a comanda nova', () => {
  const depois = sincronizar(base, pedidoDaApi(doServidor('entregue', cobrancaMp('Paga', { pagaEm: '2026-09-29T14:55:00', valorPago: 93 }))))
  assert.deepEqual(depois.conversas[0].pedido, rascunho)
})

confere('cardápio da vitrine substitui o da massa', () => {
  const depois = reducer(base, { tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: [{ sku: LASANHA, nome: 'Lasanha', preco: 85 }] })
  assert.deepEqual(depois.catalogo.cardapio.map((i) => i.sku), [LASANHA])
})

console.log(`\n${passou} verificações passaram.`)
