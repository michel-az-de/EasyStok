/* eslint-disable no-console */
// Prova da #1491 (M2.2): produção do dia no console (modo API).
//   - sobra e erros da linha como a API (US-061);
//   - produzir manda os pratos com Idempotency-Key, que só troca depois de gravar;
//   - etiquetas do lote vêm do payload do EasyStok; M2 ganha a tela.
//
//   node ferramentas/prova-1491-producao-do-dia.mjs

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

const { sobraDaLinha, erroDaLinha } = await import('../src/dominio/producao.js')
const { criarProducaoDoDia } = await import('../src/aplicacao/producaoDoDia.js')
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
  globalThis.fetch = async (url, { method = 'GET', body, headers = {} } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null, chave: headers['Idempotency-Key'] ?? null })
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

const linha = (extra = {}) => ({ sku: 'i-1', porcoes: 2, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 5, ...extra })

await confere('sobra como a US-061: 1.000 g em 2×500 sobra 0; 1.144 g sobra 144', () => {
  assert.equal(sobraDaLinha(linha({ pesoRealG: 1000 })), 0)
  assert.equal(sobraDaLinha(linha({ pesoRealG: 1144 })), 144)
  assert.equal(sobraDaLinha(linha()), null, 'sem peso real não há sobra')
})

await confere('erros da linha antes de chamar a API', () => {
  assert.equal(erroDaLinha(linha()), null)
  assert.match(erroDaLinha(linha({ sku: '' })), /prato/)
  assert.match(erroDaLinha(linha({ porcoes: 0 })), /porções/)
  assert.match(erroDaLinha(linha({ pesoRealG: 900 })), /não fecha/)
})

const ROTA = (m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao')
const RESPOSTA = { data: { loteId: 'l-1', codigoLote: 'LOT-261009-001', totalEtiquetas: 2,
  pratos: [{ cardapioItemId: 'i-1', nome: 'Ravióli', porcoes: 2, sobraG: 144 }] } }

await confere('produzir manda os pratos com a chave e devolve o lote', async () => {
  const chamadas = montarFetch([[ROTA, { status: 201, ...RESPOSTA }]])
  const p = criarProducaoDoDia({ gerarChave: () => 'k-1' })
  const r = await p.produzir([linha({ pesoRealG: 1144 })])
  assert.equal(r.ok, true)
  assert.equal(r.codigo, 'LOT-261009-001')
  assert.equal(r.pratos[0].sobraG, 144)
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.equal(post.chave, 'k-1')
  assert.deepEqual(post.corpo.pratos, [{ cardapioItemId: 'i-1', porcoes: 2, pesoPorPorcaoG: 500, pesoRealG: 1144, validadeDias: 5 }])
})

await confere('falha de rede: a nova tentativa reusa a mesma chave; depois de gravar, chave nova', async () => {
  let n = 0
  const p = criarProducaoDoDia({ gerarChave: () => `k-${++n}` })
  globalThis.fetch = async () => { throw new Error('offline') }
  assert.equal((await p.produzir([linha()])).ok, false)
  const chamadas = montarFetch([[ROTA, { status: 201, ...RESPOSTA }]])
  await p.produzir([linha()])
  await p.produzir([linha()])
  assert.deepEqual(chamadas.filter((c) => c.metodo === 'POST').map((c) => c.chave), ['k-1', 'k-2'])
})

await confere('linha inválida não chama a API', async () => {
  const chamadas = montarFetch([])
  const r = await criarProducaoDoDia().produzir([linha({ porcoes: 0 })])
  assert.equal(r.ok, false)
  assert.ok(!chamadas.some((c) => c.metodo === 'POST'))
})

await confere('etiquetas do lote vêm do payload do EasyStok, uma por porção', async () => {
  montarFetch([[(m, u) => m === 'GET' && u.endsWith('/api/lotes/l-1/etiquetas/render'), { data: { etiquetas: [
    { id: 'e1', sequencial: 1, codigo: 'C1', produto: { nome: 'Ravióli', pesoG: 500, fichaAlergenos: ['glúten'] }, loteCodigo: 'LOT-1', loteValidadeEm: '2026-10-14T00:00:00', loteCriadoEm: '2026-10-09T12:00:00' },
    { id: 'e2', sequencial: 2, codigo: 'C2', produto: { nome: 'Ravióli', pesoG: 500, fichaAlergenos: [] }, loteCodigo: 'LOT-1', loteValidadeEm: null, loteCriadoEm: '2026-10-09T12:00:00' },
  ] } }]])
  const lista = await criarProducaoDoDia().etiquetas('l-1')
  assert.equal(lista.length, 2)
  assert.deepEqual(lista[0].alergenos, ['glúten'])
  assert.equal(lista[0].lote, 'LOT-1')
})

await confere('M2 ganha a tela Produção do dia só no modo API', () => {
  const tela = moduloPorId('producao').telas.find((t) => t.id === 'producao')
  assert.equal(tela?.aba, 'producao-do-dia')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
