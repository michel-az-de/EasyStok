/* eslint-disable no-console */
// Prova da #1523 (M2.7): embalagens da produção no console (modo API).
//   - cadastrar embalagem manda a marca e conta por unidade; insumo comum segue igual;
//   - marcar ou desmarcar embalagem não mexe no mínimo nem no custo;
//   - a lista diz qual é embalagem, e a abaixo do mínimo vai para Comprar.
//
//   node ferramentas/prova-1523-embalagens.mjs

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

const { criarGestaoInsumos, lerInsumos } = await import('../src/aplicacao/insumos.js')

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
  const gestao = criarGestaoInsumos({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  return { gestao, chamadas, erros }
}

const BASE = '/api/atendimento/producao/insumos'

await confere('cadastrar embalagem manda a marca e conta por unidade', async () => {
  const m = montar([[(mt, u) => mt === 'POST' && u.endsWith(BASE), { status: 201, data: { produtoId: 'b-1' } }]])
  assert.equal(await m.gestao.criar({ nome: 'Bandeja 800 g', unidade: 'G', minimo: '50', custo: '0,4', embalagem: true }), true)
  assert.deepEqual(m.chamadas.find((c) => c.metodo === 'POST').corpo, { nome: 'Bandeja 800 g', unidade: 'Un', minimo: 50, custo: 0.4, embalagem: true })
})

await confere('insumo comum segue sem a marca no corpo', async () => {
  const m = montar([[(mt) => mt === 'POST', { status: 201, data: { produtoId: 'm-1' } }]])
  await m.gestao.criar({ nome: 'Molho', unidade: 'G' })
  assert.equal('embalagem' in m.chamadas[0].corpo, false)
})

await confere('marcar e desmarcar embalagem não mexe no mínimo nem no custo', async () => {
  const m = montar([[(mt, u) => mt === 'PUT' && u.endsWith(`${BASE}/b-1`), { status: 204 }]])
  assert.equal(await m.gestao.marcarEmbalagem('b-1', true), true)
  assert.deepEqual(m.chamadas[0].corpo, { minimo: null, custo: null, embalagem: true })
})

await confere('a lista diz qual é embalagem e a abaixo do mínimo vai para Comprar', async () => {
  montarFetch([[(mt, u) => mt === 'GET' && u.endsWith(BASE), { data: [
    { produtoId: 'b-1', nome: 'Bandeja 800 g', unidade: 'Un', saldo: 12, minimo: 50, custo: 0.4, receitas: 4, abaixoDoMinimo: true, embalagem: true },
    { produtoId: 'm-1', nome: 'Molho', unidade: 'G', saldo: 3000, minimo: 1000, custo: 0.03, receitas: 2, abaixoDoMinimo: false },
  ] }]])
  const lista = await lerInsumos()
  assert.deepEqual(lista.map((i) => [i.id, i.embalagem, i.comprar]), [['b-1', true, true], ['m-1', false, false]])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
