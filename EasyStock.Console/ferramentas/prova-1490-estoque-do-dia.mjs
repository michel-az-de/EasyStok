/* eslint-disable no-console */
// Prova da #1490 (M2.1): estoque do dia no console (modo API).
//   - lê pratos em porções, lotes (vencendo/vencido) e alertas de venda sem saldo;
//   - ajustar manda a contagem com o motivo pela rota do prato e relê;
//   - sem contagem, sem motivo ou sem prato não chama a API; M2 ganha a tela.
//
//   node ferramentas/prova-1490-estoque-do-dia.mjs

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

const { criarEstoqueDoDia, lerEstoqueDoDia } = await import('../src/aplicacao/estoqueDoDia.js')
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

const ESTOQUE = [(m, u) => m === 'GET' && u.endsWith('/api/atendimento/producao/estoque-do-dia'), { data: {
  pratos: [{ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Lasanha', porcao: '1 kg', saldo: 8, descoberto: 1, lotes: [
    { codigo: 'LOT-AMANHA', quantidade: 3, validadeEm: '2026-10-10T00:00:00', diasParaVencer: 1, vencendo: true, vencido: false },
  ] }],
  alertas: [
    { produtoId: 'p-1', cardapioItemId: 'i-1', nome: 'Lasanha', texto: 'Vendeu 1 Lasanha sem produção lançada.', quantidadeDescoberta: 1 },
    { produtoId: 'p-9', cardapioItemId: null, nome: 'Insumo', texto: 'Vendeu 1 Insumo sem produção lançada.', quantidadeDescoberta: 1 },
  ],
} }]

await confere('lê pratos em porções, lotes e alertas ligados ao prato', async () => {
  montarFetch([ESTOQUE])
  const r = await lerEstoqueDoDia()
  assert.equal(r.pratos[0].saldo, 8)
  assert.deepEqual(r.pratos[0].lotes[0], { codigo: 'LOT-AMANHA', quantidade: 3, validade: '2026-10-10', diasParaVencer: 1, vencendo: true, vencido: false })
  assert.deepEqual(r.alertas.map((a) => a.sku), ['i-1', null])
  assert.match(r.alertas[0].texto, /sem produção/)
})

function montar(rotas) {
  const chamadas = montarFetch(rotas)
  const erros = []
  let relidas = 0
  const estoque = criarEstoqueDoDia({ recarregar: async () => { relidas += 1 }, aoErro: (m) => erros.push(m) })
  return { estoque, chamadas, erros, relidas: () => relidas }
}

await confere('ajustar manda a contagem e o motivo pela rota do prato e relê', async () => {
  const m = montar([[(mt, u) => mt === 'POST' && u.endsWith('/comanda/cardapio/i-1/saldo'), { data: { quantidadeAtual: 5 } }]])
  assert.equal(await m.estoque.ajustar('i-1', '5', ' contei o congelador '), true)
  const post = m.chamadas.find((c) => c.metodo === 'POST')
  assert.deepEqual(post.corpo, { quantidadeContada: 5, motivo: 'contei o congelador' })
  assert.equal(m.relidas(), 1)
})

await confere('sem motivo, sem contagem ou produto fora do cardápio não chama a API', async () => {
  const m = montar([])
  assert.equal(await m.estoque.ajustar('i-1', '5', '  '), false)
  assert.equal(await m.estoque.ajustar('i-1', '', 'contei'), false)
  assert.equal(await m.estoque.ajustar(null, '5', 'contei'), false)
  assert.ok(!m.chamadas.some((c) => c.metodo === 'POST'))
  assert.equal(m.erros.length, 3)
})

await confere('erro do EasyStok vira aviso e relê', async () => {
  const m = montar([[(mt) => mt === 'POST', { status: 400, error: { code: 'VALIDATION', message: 'Produto sem lote de estoque: registre uma entrada em vez de ajustar.' } }]])
  assert.equal(await m.estoque.ajustar('i-1', '2', 'contei'), false)
  assert.ok(m.erros.some((e) => /registre uma entrada/.test(e)))
  assert.equal(m.relidas(), 1)
})

await confere('M2 ganha a tela Estoque do dia só no modo API', () => {
  const tela = moduloPorId('producao').telas.find((t) => t.id === 'estoque')
  assert.equal(tela?.aba, 'estoque-do-dia')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
