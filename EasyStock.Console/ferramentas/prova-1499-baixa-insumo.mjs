/* eslint-disable no-console */
// Prova da #1499 (M2.4b): baixa de insumo pela receita na produção (modo API).
//   - a receita mostra e liga/desliga a baixa automática (Gerente);
//   - ligar sem receita vira aviso do EasyStok;
//   - a produção devolve os avisos de falta de insumo e não trava.
//
//   node ferramentas/prova-1499-baixa-insumo.mjs

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

const { lerReceitas, criarGestaoReceitas } = await import('../src/aplicacao/receitas.js')
const { criarProducaoDoDia } = await import('../src/aplicacao/producaoDoDia.js')

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

function montar(rotas) {
  const chamadas = montarFetch(rotas)
  const erros = []
  let relidas = 0
  const gestao = criarGestaoReceitas({ recarregar: async () => { relidas += 1 }, aoErro: (m) => erros.push(m) })
  return { gestao, chamadas, erros, relidas: () => relidas }
}

await confere('a lista diz qual prato baixa insumo ao produzir', async () => {
  montarFetch([[(m, u) => m === 'GET' && u.endsWith('/api/atendimento/producao/receitas'), { data: [
    { cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Lasanha', rendimentoBase: 6, rendimentoUnidade: 'Un', linhas: 2, baixaAutomatica: true },
    { cardapioItemId: 'i-2', produtoId: 'p-2', nome: 'Nhoque', rendimentoBase: 1, rendimentoUnidade: 'Un', linhas: 1 },
  ] }]])
  assert.deepEqual((await lerReceitas()).map((r) => [r.produtoId, r.baixaAutomatica]), [['p-1', true], ['p-2', false]])
})

await confere('ligar a baixa manda PUT com ligada e relê', async () => {
  const m = montar([[(mt, u) => mt === 'PUT' && u.endsWith('/receitas/p-1/baixa-automatica'), { status: 204 }]])
  assert.equal(await m.gestao.marcarBaixa('p-1', true), true)
  assert.deepEqual(m.chamadas.find((c) => c.metodo === 'PUT').corpo, { ligada: true })
  assert.equal(m.relidas(), 1)
})

await confere('prato sem receita: o 400 do EasyStok vira aviso', async () => {
  const m = montar([[(mt) => mt === 'PUT', { status: 400, error: { code: 'VALIDACAO', message: 'Monte a receita antes de ligar a baixa automática.' } }]])
  assert.equal(await m.gestao.marcarBaixa('p-2', true), false)
  assert.ok(m.erros.some((e) => /receita/.test(e)))
  assert.equal(m.relidas(), 1)
})

await confere('produzir devolve os avisos de falta de insumo sem travar', async () => {
  montarFetch([[(m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao'), { status: 201, data: {
    loteId: 'l-1', codigoLote: 'LOT-261009-001', totalEtiquetas: 2,
    pratos: [{ cardapioItemId: 'i-1', nome: 'Lasanha', porcoes: 2, sobraG: null }],
    avisos: ['Faltou 100 G de Molho: ficou descoberto no estoque.'],
  } }]])
  const r = await criarProducaoDoDia({ gerarChave: () => 'k-1' })
    .produzir([{ sku: 'i-1', porcoes: 2, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 5 }])
  assert.equal(r.ok, true)
  assert.deepEqual(r.avisos, ['Faltou 100 G de Molho: ficou descoberto no estoque.'])
})

await confere('sem avisos na resposta, a lista vem vazia', async () => {
  montarFetch([[(m) => m === 'POST', { status: 201, data: { loteId: 'l-2', codigoLote: 'LOT-2', totalEtiquetas: 1, pratos: [] } }]])
  const r = await criarProducaoDoDia({ gerarChave: () => 'k-2' })
    .produzir([{ sku: 'i-1', porcoes: 1, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 5 }])
  assert.deepEqual(r.avisos, [])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
