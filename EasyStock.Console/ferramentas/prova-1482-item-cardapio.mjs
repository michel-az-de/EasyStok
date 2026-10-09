/* eslint-disable no-console */
// Prova da #1482 (M1.2): item do cardápio arquivado, em validação e com novidade.
//   - o corpo do item só leva o que veio (null = não mexe) e a novidade em data;
//   - validar e tirar/repor na gestão gravam pelas rotas novas;
//   - o cardápio da comanda lê em validação e novidade; o detalhe traz a ficha.
//
//   node ferramentas/prova-1482-item-cardapio.mjs

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

const { corpoDoItem, detalheDaApi, itemGestaoDaApi } = await import('../src/infra/api/cardapioApi.js')
const { produtoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarGestaoCardapio } = await import('../src/aplicacao/gestaoCardapio.js')
const { criarAcoesCardapioApi } = await import('../src/aplicacao/api/cardapio.js')
const { estaEmValidacao, ehNovidade } = await import('../src/dominio/cardapio.js')

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

await confere('corpo do item leva só o que veio; novidade vira data e "" tira', () => {
  const base = { nome: 'Torta', linha: 'casa', porcao: '6 fatias', preco: 60 }
  assert.deepEqual(corpoDoItem(base), { nome: 'Torta', linha: 'PrepararEmCasa', porcao: '6 fatias', preco: 60 })
  assert.deepEqual(corpoDoItem({ ...base, alergenos: 'glúten', novidadeAte: '2026-10-20T23:59:59-03:00' }),
    { nome: 'Torta', linha: 'PrepararEmCasa', porcao: '6 fatias', preco: 60, alergenos: 'glúten', novidadeAte: '2026-10-20' })
  assert.equal(corpoDoItem({ ...base, novidadeAte: null }).novidadeAte, '', 'tirar a novidade')
})

await confere('cardápio da comanda lê em validação e a novidade no fim do dia da loja', () => {
  const p = produtoDaApi({ id: 'i', nome: 'Torta', precoCentavos: 6000, linha: 'prepararEmCasa', emValidacao: true, novidadeAte: '2026-10-20' })
  assert.equal(estaEmValidacao(p), true)
  assert.equal(p.novidadeAte, '2026-10-20T23:59:59-03:00')
  assert.equal(ehNovidade(p, Date.parse('2026-10-20T23:00:00-03:00')), true)
  assert.equal(ehNovidade(p, Date.parse('2026-10-21T00:30:00-03:00')), false, 'some sozinha depois do prazo')
})

await confere('detalhe traz a ficha para o formulário', () => {
  const d = detalheDaApi({ ingredientes: 'massa, ragu', alergenos: 'glúten', tempoPreparoMinutos: 40, novidadeAte: null })
  assert.equal(d.ingredientes, 'massa, ragu')
  assert.equal(d.alergenos, 'glúten')
  assert.equal(d.tempoPreparoMinutos, 40)
})

await confere('validar e repor na gestão gravam pelas rotas novas', async () => {
  const chamadas = montarFetch([[(m) => m === 'POST', { data: {} }]])
  const itens = [itemGestaoDaApi({ cardapioItemId: 'a', nome: 'Torta', linha: 'ParaServir', preco: 60, visivel: true,
    disponivel: true, ordem: 1, controlaSaldo: false, arquivado: true, emValidacao: true })]
  const gestao = criarGestaoCardapio({ obterItens: () => itens, recarregar: async () => {}, aoErro: () => {} })
  await gestao.validar('a')
  await gestao.alternarArquivado('a')
  assert.deepEqual(chamadas.filter((c) => c.metodo === 'POST').map((c) => [c.url.split('/').slice(-2).join('/'), c.corpo]),
    [['a/validar', null], ['a/arquivar', { arquivado: false }]])
})

await confere('validar pelo balcão libera o item e relê o cardápio', async () => {
  const chamadas = montarFetch([[(m, u) => m === 'POST' && u.endsWith('/i-1/validar'), { status: 204 }]])
  const despachados = []
  const acoes = criarAcoesCardapioApi({ despachar: (a) => despachados.push(a), estadoRef: { current: { catalogo: { cardapio: [] } } } })
  assert.equal(await acoes.confirmarValidacaoItem('i-1'), true)
  assert.ok(chamadas.some((c) => c.metodo === 'POST' && c.url.endsWith('/i-1/validar')))
  assert.ok(chamadas.some((c) => c.metodo === 'GET' && c.url.endsWith('/comanda/cardapio')), 'relê')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
