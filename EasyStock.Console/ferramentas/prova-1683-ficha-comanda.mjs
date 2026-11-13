/* eslint-disable no-console */
// Prova da #1683 (design sprint do Console, Ficha e comanda): o que a Ficha mostra do pedido e da
// escolha do cliente pelo link. Só função pura de `dominio/pedido.js` e a tradução de
// `infra/api/comandaApi.js`; os componentes só chamam essas funções.
//
//   node ferramentas/prova-1683-ficha-comanda.mjs

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

const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const pedido = await import('../src/dominio/pedido.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const funcao = (nome) => {
  assert.equal(typeof pedido[nome], 'function', `dominio/pedido.js ainda não exporta ${nome}`)
  return pedido[nome]
}

const LASANHA = '11111111-1111-1111-1111-111111111111'
const SUMIDO = '44444444-4444-4444-4444-444444444444'
const PEDIDO = 'abcdef12-3333-3333-3333-333333333333'
const LINHAS = { servir: { rotulo: 'Para servir' }, casa: { rotulo: 'Preparar em casa' } }
const CARDAPIO = [{ sku: LASANHA, nome: 'Lasanha clássica', preco: 90, porcao: '600 g', linha: 'servir', estoque: null }]
const DA_API = {
  pedidoId: PEDIDO, status: 'aguardando_pagamento', total: 215, frete: 10, observacoes: 'Tocar o interfone 12',
  itens: [
    { cardapioItemId: LASANHA, nome: 'Lasanha', quantidade: 1, precoUnitario: 85, observacao: null },
    { cardapioItemId: SUMIDO, nome: 'Rondelli de queijo', quantidade: 2, precoUnitario: 60, observacao: 'sem noz', porcao: '500 g' },
  ],
  cobranca: null,
}

// 1. Conflito de edição: a operadora mantém só o que ela editou.
confere('manter minha edição conserva o servidor e reaplica itens, observação e horário editado', () => {
  const servidor = {
    itens: [{ cardapioItemId: LASANHA, qtd: 3 }], observacoes: 'do cliente', pedidoId: PEDIDO, pedidoAtualizadoEm: 't2',
    endereco: { logradouro: 'Rua Nova', numero: '10' }, janelaId: 'janela-nova', dataEntrega: '2026-10-14', cupom: 'NOVO10',
  }
  const operadora = {
    itens: [{ cardapioItemId: LASANHA, qtd: 1, observacao: 'bem passada' }], observacoes: 'Molho à parte', pedidoId: PEDIDO, pedidoAtualizadoEm: 't1',
    endereco: { logradouro: 'Rua Velha', numero: '1' }, janelaId: 'janela-velha', dataEntrega: '2026-10-13', cupom: null,
  }
  const mantido = funcao('rascunhoMantendoEdicao')(servidor, operadora)
  assert.deepEqual(mantido, { ...servidor, itens: operadora.itens, observacoes: 'Molho à parte' })
  assert.equal(mantido.endereco.logradouro, 'Rua Nova', 'endereço novo do cliente não volta ao antigo')
  assert.equal(mantido.cupom, 'NOVO10')
  const manter = funcao('rascunhoMantendoEdicao')
  const horarioLocal = { ...operadora, janelaId: 'janela-operadora', dataEntrega: '2026-10-21' }
  const comHorario = manter(servidor, horarioLocal, operadora)
  assert.equal(comHorario.janelaId, 'janela-operadora', 'janela editada localmente deve sobreviver ao conflito')
  assert.equal(comHorario.dataEntrega, '2026-10-21', 'data editada acompanha a janela local')
  assert.deepEqual(comHorario.endereco, servidor.endereco, 'preservar horário não reaplica endereço antigo')
  assert.equal(comHorario.cupom, servidor.cupom)
  const soItens = manter(servidor, operadora, { ...operadora, itens: [] })
  assert.equal(soItens.janelaId, servidor.janelaId, 'editar só itens mantém a nova janela do cliente')
  assert.equal(soItens.dataEntrega, servidor.dataEntrega)
  assert.equal(manter(servidor, operadora, operadora).janelaId, servidor.janelaId, 'voltar ao horário de origem não reaplica horário antigo')
  assert.equal(manter(servidor, { ...operadora, dataEntrega: '2026-10-21' }, operadora).dataEntrega, '2026-10-21', 'trocar só o dia também conta como edição')
  assert.equal(manter(servidor, { ...operadora, janelaId: null, dataEntrega: null }, operadora).janelaId, null, 'limpar horário localmente também é edição')
})

// 2. Prato fora do cardápio visível continua na Ficha, com o retrato do pedido.
confere('pedidoDaApi guarda o retrato da linha (nome, preço unitário, porção)', () => {
  const p = pedidoDaApi(DA_API)
  assert.equal(p.itens[1].nome, 'Rondelli de queijo', 'nome do retrato não foi mapeado')
  assert.equal(p.itens[1].precoUnitario, 60)
  assert.equal(p.itens[1].porcao, '500 g')
  assert.equal(p.totalApi, 215, 'o total é o do servidor')
})

confere('linha sem produto no cardápio aparece com nome e preço do pedido', () => {
  const p = pedidoDaApi(DA_API)
  const itens = pedido.itensDetalhados(p, CARDAPIO)
  assert.equal(itens[1].produto?.nome, 'Rondelli de queijo', 'linha fora do cardápio ficou sem produto')
  assert.equal(itens[1].produto.porcao, '500 g')
  const grupos = pedido.agruparPorLinha(itens, LINHAS)
  assert.equal(grupos.flatMap((g) => g.itens).length, 2, 'a linha fora do cardápio sumiu dos grupos')
  assert.equal(grupos.at(-1).rotulo, 'Fora do cardápio')
  assert.equal(grupos[0].itens[0].produto.nome, 'Lasanha clássica', 'com produto no cardápio, o cardápio segue valendo')
  const preco = funcao('precoDaLinha')
  assert.equal(preco(itens[0]), 85, 'preço da linha é o do pedido, não o atual do cardápio')
  assert.equal(preco(itens[1]), 120)
  assert.equal(preco({ qtd: 2, produto: { preco: 30 } }), 60, 'sem retrato (rascunho local), vale o cardápio')
})

confere('o total não muda com o retrato', () => {
  const p = pedidoDaApi(DA_API)
  assert.equal(pedido.totalDoPedido(p, CARDAPIO), 90, 'total local segue só pelo cardápio')
  assert.equal(pedido.totalDoPedido({ itens: [{ sku: LASANHA, qtd: 2 }] }, CARDAPIO), 180)
  assert.equal(pedido.agruparPorLinha([{ sku: 'x', qtd: 1, produto: null }], LINHAS).length, 0, 'linha sem retrato segue fora')
})

// 3. Observação do pedido.
confere('pedidoDaApi mapeia a observação do pedido', () => {
  assert.equal(pedidoDaApi(DA_API).observacoes, 'Tocar o interfone 12')
  assert.equal(pedidoDaApi({ ...DA_API, observacoes: null }).observacoes, '')
})

// 4. Rótulo humano do estado no histórico.
confere('estado do histórico vira rótulo humano', () => {
  const rotulo = funcao('rotuloDoEstado')
  assert.equal(rotulo('entregue'), 'Entregue')
  assert.equal(rotulo('pago'), 'Pago')
  assert.equal(rotulo('cancelado'), 'Cancelado')
  assert.equal(rotulo('aguardando pagamento'), 'Aguardando pagamento')
  assert.equal(rotulo('saiu para entrega'), 'Saiu para entrega', '#1686-8: o vocabulário da Cozinha')
  assert.equal(rotulo('preparando'), 'Em preparo')
  assert.equal(rotulo('pronto'), 'Pronto', '#1686-8: o vocabulário da Cozinha')
  assert.equal(rotulo('estado novo'), 'Estado novo', 'desconhecido sai ao menos com maiúscula')
  assert.equal(rotulo(undefined), '')
})

// 5. Uma comanda só: enquanto o cliente escolhe pelo link, a de baixo não edita.
confere('clienteEscolhendoPeloLink inclui seleção editável com itens ou ajuda e sem pedido', () => {
  const escolhendo = funcao('clienteEscolhendoPeloLink')
  const comanda = { podeEditar: true, pedido: null, rascunho: { itens: [{ cardapioItemId: LASANHA, qtd: 1 }] } }
  assert.equal(escolhendo([comanda], null), true)
  assert.equal(escolhendo([comanda], { itens: [] }), true, 'rascunho local vazio não é pedido')
  assert.equal(escolhendo([comanda], { pedidoId: PEDIDO, itens: [] }), false, 'com pedido, vale a comanda do pedido')
  assert.equal(escolhendo([{ ...comanda, pedido: { total: 10 } }], null), false)
  assert.equal(escolhendo([{ ...comanda, podeEditar: false }], null), false)
  assert.equal(escolhendo([{ ...comanda, rascunho: { itens: [] } }], null), false)
  assert.equal(escolhendo([comanda], { itens: [{ sku: LASANHA, qtd: 1 }] }), false, 'item que a atendente já pôs não fica preso')
  assert.equal(escolhendo([], null), false)
  assert.equal(escolhendo(undefined, null), false)
  const ajudaVazia = { ...comanda, ajudaSolicitada: true, rascunho: { itens: [] } }
  assert.equal(escolhendo([ajudaVazia], null), true, 'ajuda mantém uma seleção compartilhada antes do primeiro item')
  assert.equal(escolhendo([ajudaVazia], { itens: [] }), true)
  assert.equal(escolhendo([ajudaVazia], { itens: [{ sku: LASANHA, qtd: 1 }] }), false, 'não descarta rascunho local já iniciado')
  assert.equal(escolhendo([{ ...ajudaVazia, podeEditar: false }], null), false)
  assert.equal(escolhendo([ajudaVazia], { pedidoId: PEDIDO, itens: [] }), false)
  assert.equal(pedido.LINHA_CLIENTE_ESCOLHENDO,
    'O cliente está escolhendo pelo link. Abra Revisar itens para acompanhar os itens antes de confirmar o pedido.')
})

// 6. Total sem frete avisa que é sem frete.
confere('rótulo do total da seleção diz quando o frete ainda não entrou', () => {
  const rotulo = funcao('rotuloDoTotalDaSelecao')
  assert.equal(rotulo({ pedido: null, freteCombinado: null }), 'Total dos itens, sem frete')
  assert.equal(rotulo({ pedido: null }), 'Total dos itens, sem frete')
  assert.equal(rotulo({ pedido: null, freteCombinado: 0 }), 'Total previsto', 'frete combinado de zero já é frete')
  assert.equal(rotulo({ pedido: { total: 50 }, freteCombinado: null }), 'Total previsto')
  assert.equal(rotulo({ pedido: { total: 50 } }, true), 'Total dos itens salvos')
  assert.equal(rotulo({ pedido: null }, true), 'Total dos itens salvos, sem frete')
})

console.log(`\n${passou} conferências passaram.`)
