/* eslint-disable no-console */
// Prova da #1502 (M2.5): planejamento da produção no console (modo API).
//   - a sugestão (mínimo + agendados + descoberto − saldo) vem do EasyStok e ela edita;
//   - planejar calcula insumos e faltas; a lista de compras grava pela API de listas;
//   - "lançar como produção" leva as porções para a Produção do dia.
//
//   node ferramentas/prova-1502-planejamento.mjs

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

const { lerSugestao, erroDasPorcoes, planejar, itensDeCompra, gerarCompras, levarParaProducao, retirarProducaoPlanejada } = await import('../src/aplicacao/planejamento.js')
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

await confere('a sugestão vem do EasyStok, com a data e o porquê de cada prato', async () => {
  const chamadas = montarFetch([[(m, u) => m === 'GET' && u.includes('/api/atendimento/producao/sugestao'), { data: {
    ate: '2026-10-10',
    pratos: [{ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'lasanha', minimo: 5, saldo: 4, agendados: 2, descoberto: 0, sugestao: 3 }],
  } }]])
  const r = await lerSugestao('2026-10-10')
  assert.equal(r.ate, '2026-10-10')
  assert.deepEqual(r.pratos[0], { sku: 'i-1', produtoId: 'p-1', nome: 'lasanha', minimo: 5, saldo: 4, agendados: 2, descoberto: 0, sugestao: 3 })
  assert.ok(chamadas[0].url.endsWith('/sugestao?ate=2026-10-10'))
})

await confere('porções: inteiro, sem negativo e ao menos um prato', () => {
  assert.equal(erroDasPorcoes([{ porcoes: '3' }, { porcoes: '' }]), null)
  assert.match(erroDasPorcoes([{ porcoes: '0' }]), /ao menos um prato/)
  assert.match(erroDasPorcoes([{ porcoes: '-1' }]), /inteiro/)
  assert.match(erroDasPorcoes([{ porcoes: '1,5' }]), /inteiro/)
})

await confere('planejar manda só os pratos com porções e lê insumos, faltas e pendentes', async () => {
  const chamadas = montarFetch([[(m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao/planejamento'), { data: {
    itens: [{ produtoFinalId: 'p-1', status: 'Ok' }, { produtoFinalId: 'p-2', status: 'SemReceita', erro: null }],
    consolidado: [{ insumoId: 's-1', insumoNome: 'Molho', precisa: 2.4, unidadeReceita: 'Kg', saldo: 1000, unidadeSaldo: 'G', falta: 1.4, custoEstimado: 72 }],
    tudoDisponivel: false, custoEstimadoTotal: 72,
  } }]])
  const r = await planejar([{ produtoId: 'p-1', porcoes: '12' }, { produtoId: 'p-2', porcoes: '2' }, { produtoId: 'p-3', porcoes: '0' }])
  assert.deepEqual(chamadas[0].corpo, { pratos: [{ produtoId: 'p-1', porcoes: 12 }, { produtoId: 'p-2', porcoes: 2 }] })
  assert.equal(r.insumos[0].falta, 1.4)
  assert.equal(r.insumos[0].unidade, 'Kg')
  assert.deepEqual(r.pendentes, [{ produtoId: 'p-2', status: 'SemReceita', erro: null }])
  assert.equal(r.custoTotal, 72)
})

await confere('o que comprar: a falta, e o que repõe o mínimo de quem não falta, sem repetir', () => {
  const planejados = [
    { insumoId: 's-1', nome: 'Molho', falta: 1.4, unidade: 'Kg' },
    { insumoId: 's-2', nome: 'Massa', falta: null, unidade: 'G' },
  ]
  const cadastrados = [
    { id: 's-1', nome: 'Molho', comprar: true, minimo: 2000, saldo: 1000, unidade: 'G' },
    { id: 's-3', nome: 'Bandeja', comprar: true, minimo: 50, saldo: 20, unidade: 'Un' },
    { id: 's-4', nome: 'Recheio', comprar: false, minimo: 10, saldo: 30, unidade: 'G' },
  ]
  assert.deepEqual(itensDeCompra(planejados, cadastrados).map((i) => [i.produtoId, i.quantidade, i.unidade, i.categoria]),
    [['s-1', 1.4, 'Kg', 'Produção'], ['s-3', 30, 'Un', 'Mínimo']])
})

await confere('gerar a lista grava pela API de listas de compras, com origem console', async () => {
  const chamadas = montarFetch([[(m, u) => m === 'POST' && u.endsWith('/api/listas-compras/gerar'), { status: 201, data: { id: 'l-1', nome: 'Compras da produção de 10/10' } }]])
  const r = await gerarCompras([{ texto: 'Molho', produtoId: 's-1', quantidade: 1.4, unidade: 'Kg', categoria: 'Produção' }], '2026-10-10')
  assert.equal(r.ok, true)
  assert.equal(r.itens, 1)
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.equal(post.corpo.nome, 'Compras da produção de 10/10')
  assert.equal(post.corpo.origem, 'console')
  assert.equal(post.corpo.itens[0].produtoId, 's-1')
})

await confere('nada a comprar não chama a API', async () => {
  const chamadas = montarFetch([])
  const r = await gerarCompras([], '2026-10-10')
  assert.equal(r.ok, false)
  assert.equal(chamadas.length, 0)
})

await confere('lançar como produção leva só as porções planejadas, uma vez', () => {
  assert.equal(levarParaProducao([{ sku: 'i-1', porcoes: '3' }, { sku: 'i-2', porcoes: '0' }]), 1)
  assert.deepEqual(retirarProducaoPlanejada(), [{ sku: 'i-1', porcoes: '3' }])
  assert.equal(retirarProducaoPlanejada(), null, 'a segunda leitura não repete')
})

await confere('M2 ganha a tela Planejamento só no modo API', () => {
  const tela = moduloPorId('producao').telas.find((t) => t.id === 'planejamento')
  assert.equal(tela?.aba, 'planejamento')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
