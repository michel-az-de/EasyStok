/* eslint-disable no-console */
// Prova da #1241 (F11, S45/S17): no modo API o cardápio do dia vai ao EasyStok.
//   - ligar/desligar o item hoje e ajustar o saldo gravam pelas rotas do Operador;
//   - o cardápio é relido na hora (antes, a edição local voltava em 60 s);
//   - item sem controle de saldo não chama a API; erro avisa e relê.
//
//   node ferramentas/prova-f11-cardapio-do-dia.mjs

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

const acao = await import('../src/aplicacao/acoes.js')
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { criarAcoesCardapioApi } = await import('../src/aplicacao/api/cardapio.js')

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

const CARDAPIO_API = { itens: [
  { id: 'i-1', nome: 'Lasanha', linha: 'paraServir', precoCentavos: 4500, estoqueAtual: 3, disponivel: true },
] }
const ROTA_CARDAPIO = [(m, u) => m === 'GET' && u.endsWith('/api/atendimento/comanda/cardapio'), { data: CARDAPIO_API }]

function montar(rotas) {
  const chamadas = montarFetch([...rotas, ROTA_CARDAPIO])
  const despachados = []
  const estadoRef = { current: { catalogo: { cardapio: [
    { sku: 'i-1', nome: 'Lasanha', estoque: 3, disponivelHoje: true },
    { sku: 'i-2', nome: 'Bolo do dia', estoque: null, disponivelHoje: true },
  ] } } }
  const acoes = criarAcoesCardapioApi({ despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, chamadas, despachados }
}

await confere('cardápio do dia saiu da lista de não ligadas', () => {
  assert.equal(NAO_LIGADAS.alternarDisponibilidade, undefined)
  assert.equal(NAO_LIGADAS.ajustarSaldo, undefined)
})

await confere('desligar o item hoje grava o valor e relê o cardápio', async () => {
  const { acoes, chamadas, despachados } = montar([
    [(m, u) => m === 'POST' && u.endsWith('/comanda/cardapio/i-1/disponivel'), { data: { cardapioItemId: 'i-1', disponivel: false } }],
  ])
  await acoes.alternarDisponibilidade('i-1')
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.deepEqual(post.corpo, { disponivel: false })
  assert.ok(despachados.some((a) => a.tipo === acao.ALTERNAR_DISPONIBILIDADE), 'responde na hora')
  assert.ok(despachados.some((a) => a.tipo === acao.SINCRONIZAR_CARDAPIO), 'relê do EasyStok')
})

await confere('ajustar o saldo manda a contagem absoluta com motivo', async () => {
  const { acoes, chamadas } = montar([
    [(m, u) => m === 'POST' && u.endsWith('/comanda/cardapio/i-1/saldo'), { data: { quantidadeAtual: 4 } }],
  ])
  await acoes.ajustarSaldo('i-1', +1)
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.equal(post.corpo.quantidadeContada, 4)
  assert.ok(post.corpo.motivo?.length > 0)
})

await confere('saldo nunca vai negativo', async () => {
  const { acoes, chamadas } = montar([
    [(m, u) => m === 'POST' && u.endsWith('/saldo'), { data: {} }],
  ])
  await acoes.ajustarSaldo('i-1', -10)
  assert.equal(chamadas.find((c) => c.metodo === 'POST').corpo.quantidadeContada, 0)
})

await confere('item sem controle de saldo não chama a API e avisa', async () => {
  const { acoes, chamadas, despachados } = montar([])
  await acoes.ajustarSaldo('i-2', +1)
  assert.ok(!chamadas.some((c) => c.metodo === 'POST'))
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API))
})

await confere('erro da API avisa e relê o cardápio (a tela volta ao que o EasyStok tem)', async () => {
  const { acoes, despachados } = montar([
    [(m, u) => m === 'POST' && u.endsWith('/disponivel'), { status: 403, error: { code: 'FORBIDDEN', message: 'Sem permissão' } }],
  ])
  await acoes.alternarDisponibilidade('i-1')
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API))
  assert.ok(despachados.some((a) => a.tipo === acao.SINCRONIZAR_CARDAPIO))
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
