/* eslint-disable no-console */
// Prova da #1496 (M2.3): insumos da produção no console (modo API).
//   - lista com saldo, mínimo, custo e receitas; abaixo do mínimo vai para Comprar;
//   - cadastrar e ajustar validam antes e gravam pelas rotas do Gerente;
//   - erro do EasyStok (ex.: 403) vira aviso; M2 ganha a tela.
//
//   node ferramentas/prova-1496-insumos.mjs

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

const BASE = '/api/atendimento/producao/insumos'

await confere('lê os insumos com saldo, mínimo, custo, receitas e o que comprar', async () => {
  montarFetch([[(m, u) => m === 'GET' && u.endsWith(BASE), { data: [
    { produtoId: 'p1', nome: 'Molho sugo', unidade: 'G', saldo: 1500, minimo: 2000, custo: 0.03, receitas: 3, abaixoDoMinimo: true },
    { produtoId: 'p2', nome: 'Bandeja', unidade: 'Un', saldo: 80, minimo: null, custo: null, receitas: 0, abaixoDoMinimo: false },
  ] }]])
  const lista = await lerInsumos()
  assert.deepEqual(lista.map((i) => [i.id, i.comprar, i.receitas]), [['p1', true, 3], ['p2', false, 0]])
  assert.equal(lista[1].minimo, null)
})

function montar(rotas) {
  const chamadas = montarFetch(rotas)
  const erros = []
  let relidas = 0
  const gestao = criarGestaoInsumos({ recarregar: async () => { relidas += 1 }, aoErro: (m) => erros.push(m) })
  return { gestao, chamadas, erros, relidas: () => relidas }
}

await confere('cadastrar manda nome, unidade, mínimo e custo (vírgula vira ponto) e relê', async () => {
  const m = montar([[(mt, u) => mt === 'POST' && u.endsWith(BASE), { status: 201, data: { produtoId: 'p9' } }]])
  assert.equal(await m.gestao.criar({ nome: ' Recheio de ricota ', unidade: 'G', minimo: '1000', custo: '0,05' }), true)
  assert.deepEqual(m.chamadas.find((c) => c.metodo === 'POST').corpo, { nome: 'Recheio de ricota', unidade: 'G', minimo: 1000, custo: 0.05 })
  assert.equal(m.relidas(), 1)
})

await confere('ajustar manda só mínimo e custo; vazio vira null (não mexe)', async () => {
  const m = montar([[(mt, u) => mt === 'PUT' && u.endsWith(`${BASE}/p1`), { status: 204 }]])
  await m.gestao.atualizar('p1', { minimo: '2500', custo: '' })
  assert.deepEqual(m.chamadas.find((c) => c.metodo === 'PUT').corpo, { minimo: 2500, custo: null })
})

await confere('nome vazio, mínimo quebrado ou custo negativo não chamam a API', async () => {
  const m = montar([])
  assert.equal(await m.gestao.criar({ nome: '  ' }), false)
  assert.equal(await m.gestao.criar({ nome: 'Molho', minimo: '1,5' }), false)
  assert.equal(await m.gestao.atualizar('p1', { minimo: '', custo: '-1' }), false)
  assert.ok(!m.chamadas.some((c) => c.metodo !== 'GET'))
  assert.equal(m.erros.length, 3)
})

await confere('sem ser Gerente, o 403 do EasyStok vira aviso', async () => {
  const m = montar([[(mt) => mt === 'POST', { status: 403 }]])
  assert.equal(await m.gestao.criar({ nome: 'Molho', unidade: 'G' }), false)
  assert.ok(m.erros.some((e) => /permissão/i.test(e)))
})

await confere('M2 ganha a tela Insumos só no modo API', () => {
  const tela = moduloPorId('producao').telas.find((t) => t.id === 'insumos')
  assert.equal(tela?.aba, 'insumos')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
