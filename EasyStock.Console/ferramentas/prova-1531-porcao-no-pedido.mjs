/* eslint-disable no-console */
// Prova da #1531 (M1.4b): vender por porção na comanda do console (modo API).
//   - o cardápio da comanda traz as porções; a porção é um produto com sku composto;
//   - duas porções do mesmo prato são linhas próprias, cada uma com o seu preço;
//   - a ida e a volta com a API separam e remontam prato e porção.
//
//   node ferramentas/prova-1531-porcao-no-pedido.mjs

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

const { itemPorSku, skuDaPorcao, partesDoSku, produtoDaPorcao, porcaoDoToque } = await import('../src/dominio/cardapio.js')
const { comItem, novoPedido, totalDoPedido, itensDetalhados } = await import('../src/dominio/pedido.js')
const { listarCardapio, corpoDoPedido, pedidoDaApi, listarJanelas } = await import('../src/infra/api/comandaApi.js')

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
        const { status = 200, data = null } = resposta
        return new Response(JSON.stringify({ data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const RAVIOLI = 'aaaaaaaa-0000-0000-0000-000000000001'
const P300 = 'bbbbbbbb-0000-0000-0000-000000000300'
const P800 = 'bbbbbbbb-0000-0000-0000-000000000800'
const BOLO = 'aaaaaaaa-0000-0000-0000-000000000002'
const MENU = {
  itens: [
    { id: RAVIOLI, nome: 'Ravióli', linha: 'prepararEmCasa', pesoExibicao: '300 g', precoCentavos: 2800, disponivel: true },
    { id: BOLO, nome: 'Bolo', linha: 'paraServir', pesoExibicao: 'fatia', precoCentavos: 1200, disponivel: true },
  ],
  porcoes: { [RAVIOLI]: [
    { id: P300, rotulo: '300 g', preco: 28, peso: '300 g', disponivel: true, padrao: true },
    { id: P800, rotulo: '800 g', preco: 62, peso: null, disponivel: true, padrao: false },
  ] },
}

let cardapio = []
await confere('o cardápio da comanda traz as porções de cada prato', async () => {
  montarFetch([[(m, u) => u.endsWith('/api/atendimento/comanda/cardapio'), { data: MENU }]])
  cardapio = await listarCardapio()
  assert.deepEqual(cardapio.map((i) => [i.nome, i.porcoes.length]), [['Ravióli', 2], ['Bolo', 0]])
  assert.equal(cardapio[0].porcoes[1].preco, 62)
})

await confere('a porção é um produto: nome, preço e sku composto; o prato segue achável', () => {
  const p800 = itemPorSku(cardapio, skuDaPorcao(RAVIOLI, P800))
  assert.deepEqual([p800.nome, p800.preco, p800.porcao, p800.itemSku, p800.variacaoId], ['Ravióli · 800 g', 62, '800 g', RAVIOLI, P800])
  assert.equal(itemPorSku(cardapio, RAVIOLI).nome, 'Ravióli')
  assert.deepEqual(partesDoSku(BOLO), { itemSku: BOLO, variacaoId: null })
  assert.equal(itemPorSku(cardapio, skuDaPorcao(RAVIOLI, 'porcao-que-saiu')).nome, 'Ravióli', 'porção removida mostra o prato')
  assert.equal(itemPorSku(cardapio, 'nao-existe'), null)
})

await confere('o toque no prato soma a padrão; se ela acabou, a primeira que tem; sem nenhuma, nada', () => {
  const [ravioli] = cardapio
  assert.equal(porcaoDoToque(ravioli).id, P300)
  const semPadrao = { ...ravioli, porcoes: ravioli.porcoes.map((p) => (p.id === P300 ? { ...p, disponivel: false } : p)) }
  assert.equal(porcaoDoToque(semPadrao).id, P800)
  assert.equal(porcaoDoToque({ ...ravioli, porcoes: ravioli.porcoes.map((p) => ({ ...p, disponivel: false })) }), null)
  assert.equal(produtoDaPorcao(semPadrao, semPadrao.porcoes[0]).disponivelHoje, false, 'porção esgotada não vende')
})

await confere('duas porções do mesmo prato são linhas próprias, cada uma com o seu preço', () => {
  let pedido = novoPedido('1')
  pedido = comItem(pedido, skuDaPorcao(RAVIOLI, P300))
  pedido = comItem(pedido, skuDaPorcao(RAVIOLI, P800))
  pedido = comItem(pedido, skuDaPorcao(RAVIOLI, P800))
  pedido = comItem(pedido, BOLO)
  assert.deepEqual(pedido.itens.map((l) => l.qtd), [1, 2, 1])
  assert.equal(totalDoPedido(pedido, cardapio), 28 + 2 * 62 + 12)
  assert.equal(itensDetalhados(pedido, cardapio)[1].produto.nome, 'Ravióli · 800 g')
})

await confere('o pedido vai com o prato e a porção separados; prato sem porção vai sem variacaoId', () => {
  const pedido = { itens: [{ sku: skuDaPorcao(RAVIOLI, P800), qtd: 2, obs: 'sem sal' }, { sku: BOLO, qtd: 1, obs: '' }], janela: null }
  assert.deepEqual(corpoDoPedido(pedido).itens, [
    { cardapioItemId: RAVIOLI, qtd: 2, observacao: 'sem sal', variacaoId: P800 },
    { cardapioItemId: BOLO, qtd: 1, observacao: null },
  ])
})

await confere('o pedido que volta do EasyStok remonta o sku da porção', () => {
  const p = pedidoDaApi({ pedidoId: 'cccccccc-0000-0000-0000-000000000001', status: 'aguardando', itens: [
    { cardapioItemId: RAVIOLI, quantidade: 2, observacao: null, variacaoId: P800, porcao: '800 g' },
    { cardapioItemId: BOLO, quantidade: 1, observacao: null },
  ] })
  assert.deepEqual(p.itens.map((l) => l.sku), [skuDaPorcao(RAVIOLI, P800), BOLO])
  assert.equal(totalDoPedido(p, cardapio), 2 * 62 + 12)
})

await confere('as janelas recebem o id do prato, nunca o sku composto', async () => {
  const chamadas = montarFetch([[(m, u) => u.includes('/comanda/janelas'), { data: { lojaDisponivel: true, janelas: [] } }]])
  await listarJanelas({ itens: [skuDaPorcao(RAVIOLI, P800), BOLO] })
  assert.ok(chamadas[0].url.includes(`itens=${RAVIOLI}`) && !chamadas[0].url.includes('~') && !chamadas[0].url.includes('%7E'))
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
