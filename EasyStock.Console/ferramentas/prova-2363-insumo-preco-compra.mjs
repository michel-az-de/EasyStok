/* eslint-disable no-console */
// Prova da #2363: o insumo se cadastra pelo preço de compra.
//   - "R$ 45 por 10 kg" grava R$ 0,0045 por g, sem arredondar no meio, e a receita com 350 g dá R$ 1,575;
//   - "quanto tem hoje" vai junto como saldo inicial; vazio ou zero não movimenta;
//   - a edição reabre o custo no formato de compra.
//
//   node ferramentas/prova-2363-insumo-preco-compra.mjs

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

const { compraDoCusto, criarGestaoInsumos, custoPelaCompra, unidadesDeCompra } = await import('../src/aplicacao/insumos.js')

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

const BASE = '/api/atendimento/producao/insumos'
const montar = () => {
  const chamadas = montarFetch([[(mt) => mt === 'POST', { status: 201, data: { produtoId: 'p9' } }], [(mt) => mt === 'PUT', { status: 204 }]])
  const erros = []
  const gestao = criarGestaoInsumos({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  return { gestao, chamadas, erros, corpo: (metodo) => chamadas.find((c) => c.metodo === metodo && c.url.includes(BASE))?.corpo }
}

await confere('R$ 45 por 10 kg vira R$ 0,0045 por g e 350 g na receita custam R$ 1,575', () => {
  const custo = custoPelaCompra({ preco: 45, quantidade: 10, unidadeCompra: 'Kg', unidade: 'G' })
  assert.equal(custo, 0.0045)
  assert.equal(Math.round(350 * custo * 1e6) / 1e6, 1.575)
})

await confere('a unidade de compra é da família do insumo', () => {
  assert.deepEqual(unidadesDeCompra('G'), ['Kg', 'G'])
  assert.deepEqual(unidadesDeCompra('Ml'), ['L', 'Ml'])
  assert.deepEqual(unidadesDeCompra('Un'), ['Un'])
  assert.equal(custoPelaCompra({ preco: 12, quantidade: 2, unidadeCompra: 'L', unidade: 'Ml' }), 0.006)
  assert.equal(custoPelaCompra({ preco: 30, quantidade: 12, unidadeCompra: 'Un', unidade: 'Un' }), 2.5)
})

await confere('cadastrar pelo preço pago manda o custo de 6 casas e o saldo inicial', async () => {
  const m = montar()
  assert.equal(await m.gestao.criar({ nome: 'Farinha', unidade: 'G', minimo: '', preco: '45,00', quantidade: '10', unidadeCompra: 'Kg', saldo: '2500' }), true)
  assert.deepEqual(m.corpo('POST'), { nome: 'Farinha', unidade: 'G', minimo: null, custo: 0.0045, saldoInicial: 2500 })
  assert.deepEqual(m.erros, [])
})

await confere('saldo vazio ou zero não manda saldo inicial', async () => {
  for (const saldo of ['', '0', undefined]) {
    const m = montar()
    assert.equal(await m.gestao.criar({ nome: 'Farinha', unidade: 'G', preco: '45', quantidade: '10', unidadeCompra: 'Kg', saldo }), true)
    assert.equal('saldoInicial' in m.corpo('POST'), false, `saldo ${saldo}`)
  }
})

await confere('preço sem quantidade, preço com fração de centavo e saldo negativo não gravam', async () => {
  const m = montar()
  assert.equal(await m.gestao.criar({ nome: 'Farinha', unidade: 'G', preco: '45', quantidade: '', unidadeCompra: 'Kg' }), false)
  assert.equal(await m.gestao.criar({ nome: 'Farinha', unidade: 'G', preco: '4,505', quantidade: '1', unidadeCompra: 'Kg' }), false)
  assert.equal(await m.gestao.criar({ nome: 'Farinha', unidade: 'G', preco: '45', quantidade: '10', unidadeCompra: 'Kg', saldo: '-1' }), false)
  assert.equal(m.chamadas.length, 0)
  assert.equal(m.erros.length, 3)
})

await confere('a edição reabre o custo no formato de compra e grava o mesmo valor', async () => {
  assert.deepEqual(compraDoCusto(0.0045, 'G'), { preco: 4.5, quantidade: 1, unidadeCompra: 'Kg' })
  assert.deepEqual(compraDoCusto(0.002333, 'G'), { preco: 23.33, quantidade: 10, unidadeCompra: 'Kg' })
  assert.deepEqual(compraDoCusto(1.5, 'Un'), { preco: 1.5, quantidade: 1, unidadeCompra: 'Un' })
  assert.equal(compraDoCusto(null, 'G'), null)
  const m = montar()
  const compra = compraDoCusto(0.0045, 'G')
  assert.equal(await m.gestao.atualizar('p1', { minimo: '', preco: '4,50', quantidade: String(compra.quantidade), unidadeCompra: compra.unidadeCompra, unidade: 'G' }), true)
  assert.equal(m.corpo('PUT').custo, 0.0045)
})

await confere('apagar o preço na edição limpa o custo', async () => {
  const m = montar()
  assert.equal(await m.gestao.atualizar('p1', { minimo: '10', preco: '', quantidade: '1', unidadeCompra: 'Kg', unidade: 'G' }), true)
  assert.deepEqual(m.corpo('PUT'), { minimo: 10, custo: null, limparCusto: true })
})

console.log(`
${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
