/* eslint-disable no-console */
// Prova da #1537 (M1.4c): saldo por porção no console (modo API).
//   - prato com porções exige a porção produzida, e a produção a manda ao EasyStok;
//   - duas porções do mesmo prato são duas linhas da produção;
//   - o estoque do dia mostra o saldo de cada porção.
//
//   node ferramentas/prova-1537-saldo-por-porcao.mjs

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

const { erroDaLinha } = await import('../src/dominio/producao.js')
const { criarProducaoDoDia } = await import('../src/aplicacao/producaoDoDia.js')
const { estoqueDoDiaDaApi } = await import('../src/infra/api/producaoApi.js')

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
        const { status = 200, data = null } = resposta
        return new Response(JSON.stringify({ data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const linha = (extra = {}) => ({ sku: 'i-1', variacaoId: null, exigePorcao: false, porcoes: 6, pesoPorPorcaoG: 800, pesoRealG: null, validadeDias: 5, ...extra })
const PRODUCAO = [(m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao'), { status: 201, data: {
  loteId: 'l-1', codigoLote: 'LOT-1', totalEtiquetas: 6, pratos: [{ cardapioItemId: 'i-1', nome: 'Ravióli 800 g', porcoes: 6, sobraG: null }] } }]

await confere('prato com porções exige a porção produzida; prato sem porções não', () => {
  assert.match(erroDaLinha(linha({ exigePorcao: true })), /Escolha a porção/)
  assert.equal(erroDaLinha(linha({ exigePorcao: true, variacaoId: 'v-800' })), null)
  assert.equal(erroDaLinha(linha()), null)
})

await confere('sem a porção, a produção não chama o EasyStok', async () => {
  const chamadas = montarFetch([PRODUCAO])
  const r = await criarProducaoDoDia({ gerarChave: () => 'k-1' }).produzir([linha({ exigePorcao: true })])
  assert.equal(r.ok, false)
  assert.equal(chamadas.length, 0)
})

await confere('a produção manda a porção; duas porções do mesmo prato são duas linhas', async () => {
  const chamadas = montarFetch([PRODUCAO])
  const r = await criarProducaoDoDia({ gerarChave: () => 'k-2' }).produzir([
    linha({ exigePorcao: true, variacaoId: 'v-800' }),
    linha({ exigePorcao: true, variacaoId: 'v-300', porcoes: 4, pesoPorPorcaoG: 300 }),
    linha({ sku: 'i-2', porcoes: 3, pesoPorPorcaoG: null }),
  ])
  assert.equal(r.ok, true)
  const pratos = chamadas[0].corpo.pratos
  assert.deepEqual(pratos.map((p) => [p.cardapioItemId, p.variacaoId ?? null, p.porcoes]), [['i-1', 'v-800', 6], ['i-1', 'v-300', 4], ['i-2', null, 3]])
  assert.equal('variacaoId' in pratos[2], false, 'prato sem porções vai sem o campo')
  assert.equal(r.pratos[0].nome, 'Ravióli 800 g')
})

await confere('o estoque do dia traz o saldo de cada porção e o que ficou sem porção', () => {
  const e = estoqueDoDiaDaApi({ pratos: [
    { cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Ravióli', saldo: 12, descoberto: 0, lotes: [],
      porcoes: [{ variacaoId: 'v-300', rotulo: '300 g', saldo: 4 }, { variacaoId: 'v-800', rotulo: '800 g', saldo: 6 }, { variacaoId: null, rotulo: 'sem porção', saldo: 2 }] },
    { cardapioItemId: 'i-2', produtoId: 'p-2', nome: 'Bolo', saldo: 3, descoberto: 0, lotes: [] },
  ] })
  assert.deepEqual(e.pratos[0].porcoes.map((s) => [s.rotulo, s.saldo]), [['300 g', 4], ['800 g', 6], ['sem porção', 2]])
  assert.deepEqual(e.pratos[1].porcoes, [])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
