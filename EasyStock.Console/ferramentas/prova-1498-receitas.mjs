/* eslint-disable no-console */
// Prova da #1498 (M2.4a): receitas dos pratos no console (modo API).
//   - lista e detalhe com o custo por porção do EasyStok;
//   - salvar valida antes e grava a receita inteira pelo PUT da composição;
//   - erro (ex.: 403 sem Gerente) vira aviso; M2 ganha a tela.
//
//   node ferramentas/prova-1498-receitas.mjs

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

const { lerReceitas, lerReceita, criarGestaoReceitas, erroDaReceita } = await import('../src/aplicacao/receitas.js')
const { moduloPorId } = await import('../src/dominio/modulos.js')

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

await confere('lista as receitas com o custo por porção do EasyStok', async () => {
  montarFetch([[(m, u) => m === 'GET' && u.endsWith('/api/atendimento/producao/receitas'), { data: [
    { cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Lasanha', rendimentoBase: 6, rendimentoUnidade: 'Un', linhas: 2, custoTotal: 45, custoPorRendimento: 7.5 },
    { cardapioItemId: 'i-2', produtoId: 'p-2', nome: 'Nhoque', rendimentoBase: 1, rendimentoUnidade: 'Un', linhas: 0, custoTotal: null, custoPorRendimento: null },
  ] }]])
  const lista = await lerReceitas()
  assert.deepEqual(lista.map((r) => [r.produtoId, r.custoPorRendimento]), [['p-1', 7.5], ['p-2', null]])
})

await confere('detalhe traz as linhas com o custo de cada insumo', async () => {
  montarFetch([[(m, u) => m === 'GET' && u.endsWith('/receitas/p-1'), { data: {
    produtoId: 'p-1', nome: 'Lasanha', rendimentoBase: 6, rendimentoUnidade: 'Un', unidadeMedidaBase: 'Un',
    linhas: [{ insumoId: 's-1', insumo: 'Molho', quantidade: 1.2, unidade: 'Kg', unidadeDoInsumo: 'G', custo: 36 }],
    custoTotal: 36, custoPorRendimento: 6,
  } }]])
  const d = await lerReceita('p-1')
  assert.deepEqual(d.linhas[0], { insumoId: 's-1', insumo: 'Molho', quantidade: 1.2, unidade: 'Kg', custo: 36 })
})

await confere('validação: rendimento, insumo, quantidade e insumo repetido', () => {
  const linha = { insumoId: 's-1', quantidade: '1', unidade: 'G' }
  assert.equal(erroDaReceita({ rendimento: '6', linhas: [linha] }), null)
  assert.match(erroDaReceita({ rendimento: '0', linhas: [linha] }), /rende/)
  assert.match(erroDaReceita({ rendimento: '6', linhas: [{ ...linha, insumoId: '' }] }), /insumo/)
  assert.match(erroDaReceita({ rendimento: '6', linhas: [{ ...linha, quantidade: '0' }] }), /Quantidade/)
  assert.match(erroDaReceita({ rendimento: '6', linhas: [linha, linha] }), /duas vezes/)
})

function montar(rotas) {
  const chamadas = montarFetch(rotas)
  const erros = []
  let relidas = 0
  const gestao = criarGestaoReceitas({ recarregar: async () => { relidas += 1 }, aoErro: (m) => erros.push(m) })
  return { gestao, chamadas, erros, relidas: () => relidas }
}

await confere('salvar grava a receita inteira pelo PUT da composição e relê', async () => {
  const m = montar([[(mt, u) => mt === 'PUT' && u.endsWith('/api/produtos/p-1/composicao'), { status: 204 }]])
  const ok = await m.gestao.salvar('p-1', {
    rendimento: '6', unidadeRendimento: 'Un', unidadeMedidaBase: 'Un',
    linhas: [{ insumoId: 's-1', quantidade: '1,2'.replace(',', '.'), unidade: 'Kg' }, { insumoId: 's-2', quantidade: '6', unidade: 'Un' }],
  })
  assert.equal(ok, true)
  const put = m.chamadas.find((c) => c.metodo === 'PUT')
  assert.equal(put.corpo.rendimentoBase, 6)
  assert.equal(put.corpo.rendimentoUnidade, 'Un')
  assert.deepEqual(put.corpo.linhas.map((l) => [l.insumoId, l.quantidade, l.unidade, l.ordemExibicao]), [['s-1', 1.2, 'Kg', 0], ['s-2', 6, 'Un', 1]])
  assert.equal(m.relidas(), 1)
})

await confere('sem ser Gerente, o 403 vira aviso e relê', async () => {
  const m = montar([[(mt) => mt === 'PUT', { status: 403 }]])
  assert.equal(await m.gestao.salvar('p-1', { rendimento: '6', linhas: [{ insumoId: 's-1', quantidade: '1', unidade: 'G' }] }), false)
  assert.ok(m.erros.some((e) => /permissão/i.test(e)))
  assert.equal(m.relidas(), 1)
})

await confere('M2 ganha a tela Receitas só no modo API', () => {
  const tela = moduloPorId('producao').telas.find((t) => t.id === 'receitas')
  assert.equal(tela?.aba, 'receitas')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
