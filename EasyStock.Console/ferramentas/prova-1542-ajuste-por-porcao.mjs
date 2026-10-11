/* eslint-disable no-console */
// Prova da #1542 (M1.4d): ajuste de saldo por porção no console (modo API).
//   1. o estoque do dia traz, por porção, o saldo, a venda sem saldo e o que está guardado;
//   2. só porção com lote entra na contagem, e o que ficou sem porção tem chave própria;
//   3. o formulário abre na porção que tem venda sem saldo (ou na única que existe);
//   4. a contagem começa pelo que está guardado, vencido incluído, e diz quantas são vencidas;
//   5. em prato com porções a contagem exige a porção e manda a porção ao EasyStok;
//   6. prato sem porções segue mandando a contagem do prato, como antes;
//   7. os botões − e + do cardápio do dia não contam prato com porções (a contagem é por porção).
//
//   node ferramentas/prova-1542-ajuste-por-porcao.mjs

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

const { estoqueDoDiaDaApi } = await import('../src/infra/api/producaoApi.js')
const {
  contagemInicial, criarEstoqueDoDia, porcaoSugerida, porcoesParaAjuste, vencidasNaContagem,
} = await import('../src/aplicacao/estoqueDoDia.js')
const { criarAcoesCardapioApi } = await import('../src/aplicacao/api/cardapio.js')
const acao = await import('../src/aplicacao/acoes.js')

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

function montarFetch(responder = () => ({ data: {} })) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    const chamada = { metodo: method, url, corpo: body ? JSON.parse(body) : null }
    chamadas.push(chamada)
    const { status = 200, data = null, error } = responder(chamada) ?? {}
    return new Response(JSON.stringify(error ? { error } : { data }), { status })
  }
  return chamadas
}

// O ravióli do exemplo: 300 g com 4 boas e 1 vencida e venda sem saldo; 800 g ainda sem lote; e
// um resto de estoque antigo, sem porção.
const daApi = {
  pratos: [
    {
      cardapioItemId: 'i-ravioli', produtoId: 'p-ravioli', nome: 'Ravióli', porcao: '300 g', saldo: 6, descoberto: 2, fisico: 7, lotes: [],
      porcoes: [
        { variacaoId: 'v-300', rotulo: '300 g', saldo: 4, produtoVariacaoId: 'pv-300', descoberto: 2, fisico: 5 },
        { variacaoId: 'v-800', rotulo: '800 g', saldo: 0, produtoVariacaoId: null, descoberto: 0, fisico: 0 },
        { variacaoId: null, rotulo: 'sem porção', saldo: 2, produtoVariacaoId: null, descoberto: 0, fisico: 2 },
      ],
    },
    { cardapioItemId: 'i-lasanha', produtoId: 'p-lasanha', nome: 'Lasanha', porcao: '500 g', saldo: 8, descoberto: 0, fisico: 10, lotes: [], porcoes: [] },
  ],
  alertas: [],
}
const { pratos: [ravioli, lasanha] } = estoqueDoDiaDaApi(daApi)

// --- 1. Tradução do estoque do dia ----------------------------------------------------
await confere('o prato e cada porção trazem o guardado e a venda sem saldo', () => {
  assert.equal(lasanha.fisico, 10)
  assert.deepEqual(ravioli.porcoes.map((s) => [s.rotulo, s.saldo, s.descoberto, s.fisico]),
    [['300 g', 4, 2, 5], ['800 g', 0, 0, 0], ['sem porção', 2, 0, 2]])
})
await confere('API antiga, sem os campos novos, não quebra a tela', () => {
  const antigo = estoqueDoDiaDaApi({ pratos: [{ cardapioItemId: 'x', nome: 'X', saldo: 3, descoberto: 0, porcoes: [{ variacaoId: 'v', rotulo: 'P', saldo: 3 }] }] }).pratos[0]
  assert.equal(antigo.fisico, 3, 'sem o campo, o guardado é o saldo')
  assert.deepEqual([antigo.porcoes[0].descoberto, antigo.porcoes[0].fisico], [0, 3])
})

// --- 2 e 3. Opções da contagem -------------------------------------------------------------
const opcoes = porcoesParaAjuste(ravioli)
await confere('só porção com lote entra, e a "sem porção" tem chave própria', () => {
  assert.deepEqual(opcoes.map((s) => [s.chave, s.corpo]), [
    ['v-300', { variacaoId: 'v-300' }],
    ['sem-porcao', { semPorcao: true }],
  ])
  assert.deepEqual(porcoesParaAjuste(lasanha), [])
})
await confere('o formulário abre na porção com venda sem saldo, ou na única que existe', () => {
  assert.equal(porcaoSugerida(opcoes), 'v-300')
  assert.equal(porcaoSugerida([opcoes[1]]), 'sem-porcao')
  assert.equal(porcaoSugerida(opcoes.map((s) => ({ ...s, descoberto: 0 }))), '', 'sem pista, ela escolhe')
  assert.equal(porcaoSugerida(opcoes.map((s) => ({ ...s, descoberto: 1 }))), '', 'duas com venda sem saldo: ela escolhe')
})

// --- 4. O que a contagem cobre ------------------------------------------------------------
await confere('a contagem começa pelo que está guardado e diz quantas são vencidas', () => {
  assert.equal(contagemInicial(opcoes[0]), 5)
  assert.equal(vencidasNaContagem(opcoes[0]), 1)
  assert.equal(contagemInicial(lasanha), 10)
  assert.equal(vencidasNaContagem(lasanha), 2)
  assert.equal(vencidasNaContagem(opcoes[1]), 0)
  assert.equal(contagemInicial(null), '')
})

// --- 5 e 6. A contagem que vai ao EasyStok --------------------------------------------------
function montarEstoque() {
  const chamadas = montarFetch()
  const erros = []
  const estoque = criarEstoqueDoDia({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  return { chamadas, erros, estoque }
}
await confere('prato com porções exige a porção antes de mandar', async () => {
  const m = montarEstoque()
  assert.equal(await m.estoque.ajustar('i-ravioli', '6', 'contei', { porcoes: opcoes, porcao: '' }), false)
  assert.deepEqual(m.erros, ['Escolha a porção que você contou.'])
  assert.equal(m.chamadas.length, 0)
})
await confere('a contagem da porção manda a porção; a do resto manda "sem porção"', async () => {
  const m = montarEstoque()
  assert.equal(await m.estoque.ajustar('i-ravioli', '6', 'contei', { porcoes: opcoes, porcao: 'v-300' }), true)
  assert.equal(await m.estoque.ajustar('i-ravioli', '0', 'joguei fora', { porcoes: opcoes, porcao: 'sem-porcao' }), true)
  assert.deepEqual(m.chamadas.map((c) => c.corpo), [
    { quantidadeContada: 6, motivo: 'contei', variacaoId: 'v-300' },
    { quantidadeContada: 0, motivo: 'joguei fora', semPorcao: true },
  ])
  assert.ok(m.chamadas.every((c) => c.url.endsWith('/api/atendimento/comanda/cardapio/i-ravioli/saldo')))
})
await confere('prato sem porções manda a contagem do prato, como antes', async () => {
  const m = montarEstoque()
  assert.equal(await m.estoque.ajustar('i-lasanha', '9', 'contei'), true)
  assert.deepEqual(m.chamadas[0].corpo, { quantidadeContada: 9, motivo: 'contei' })
})

// --- 7. Botões − e + do cardápio do dia ------------------------------------------------------
await confere('− e + não contam prato com porções: avisam onde acertar e não chamam a API', async () => {
  const chamadas = montarFetch()
  const despachos = []
  const cardapio = [
    { sku: 'i-ravioli', nome: 'Ravióli', estoque: 6, porcoes: [{ id: 'v-300', rotulo: '300 g' }] },
    { sku: 'i-lasanha', nome: 'Lasanha', estoque: 8, porcoes: [] },
  ]
  const acoes = criarAcoesCardapioApi({ despachar: (a) => despachos.push(a), estadoRef: { current: { catalogo: { cardapio } } } })

  assert.equal(await acoes.ajustarSaldo('i-ravioli', 1), false)
  assert.equal(chamadas.length, 0)
  assert.ok(!despachos.some((d) => d.tipo === acao.AJUSTAR_SALDO), 'o saldo da tela não muda')
  assert.match(despachos.find((d) => d.tipo === acao.AVISO_API).mensagem, /Ravióli.*porção.*Estoque do dia/)

  await acoes.ajustarSaldo('i-lasanha', 1)
  assert.deepEqual(chamadas.find((c) => c.metodo === 'POST').corpo.quantidadeContada, 9, 'prato sem porções segue contando')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
