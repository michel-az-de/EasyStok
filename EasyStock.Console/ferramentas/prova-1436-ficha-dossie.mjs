/* eslint-disable no-console */
// Prova da issue #1436: no modo API a nota interna vai ao EasyStok e a Ficha e o Histórico
// mostram o que o dossiê devolve (total de pedidos, últimos pedidos e notas), não zero fixo.
//
//   node ferramentas/prova-1436-ficha-dossie.mjs

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

const acao = await import('../src/aplicacao/acoes.js')
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { clienteDoDossie } = await import('../src/infra/api/traducaoCliente.js')
const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const CLIENTE = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const PEDIDO = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
const dossie = {
  cliente: { id: CLIENTE, nome: 'Thatiane', telefone: '+5511997573992' },
  enderecos: [],
  totalPedidos: 3,
  notas: [{ id: 'n1', texto: 'Prefere massa al dente', autor: 'Felipe', criadoEm: '2026-10-07T14:30:00Z' }],
  ultimosPedidos: [
    { id: PEDIDO, status: 'entregue', criadoEm: '2026-10-05T18:00:00Z', total: 45, itens: [{ nome: 'Lasanha Bolonhesa 800 g', quantidade: 1 }] },
    { id: 'p2', status: 'cancelado', criadoEm: '2026-09-30T18:00:00Z', total: 38, itens: [{ nome: 'Lasanha Verde 600 g', quantidade: 2 }] },
  ],
}

await confere('dossiê vira total de pedidos, notas e histórico na Ficha', () => {
  const c = clienteDoDossie(dossie)
  assert.equal(c.cliente.pedidos, 3)
  assert.equal(c.cliente.notas.length, 1)
  assert.equal(c.cliente.notas[0].texto, 'Prefere massa al dente')
  assert.equal(c.cliente.notas[0].autor, 'Felipe')
  assert.ok(c.cliente.notas[0].em)
  assert.equal(c.cliente.historico.length, 2)
  assert.deepEqual(c.cliente.historico[0].itens, ['1× Lasanha Bolonhesa 800 g'])
  assert.equal(c.cliente.historico[0].total, 45)
  assert.equal(c.cliente.historico[0].estado, 'entregue')
  assert.equal(c.cliente.historico[0].em, '2026-10-05')
  assert.ok(c.cliente.historico[0].numero)
  assert.equal(c.cliente.historico[1].estado, 'cancelado')
})

await confere('dossiê antigo sem os campos novos não quebra', () => {
  const c = clienteDoDossie({ cliente: { id: CLIENTE, nome: 'X' } })
  assert.equal(c.cliente.pedidos, 0)
  assert.deepEqual(c.cliente.notas, [])
  assert.deepEqual(c.cliente.historico, [])
})

await confere('salvarNota não está mais na lista de não ligadas', () => {
  assert.equal(NAO_LIGADAS.salvarNota, undefined)
})

function montar(conversa) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    if (method === 'POST' && url.endsWith('/notas')) return new Response(JSON.stringify({ data: { id: 'n2' } }), { status: 201 })
    if (url.endsWith('/dossie')) return new Response(JSON.stringify({ data: dossie }), { status: 200 })
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  const despachados = []
  const estadoRef = { current: { conversas: [conversa] } }
  const acoes = criarAcoesClienteApi({ despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, chamadas, despachados }
}

await confere('salvarNota grava pela API do cliente e relê o dossiê', async () => {
  const { acoes, chamadas, despachados } = montar({ id: 'conv-1', clienteId: CLIENTE, cliente: { notas: [] } })
  await acoes.salvarNota('conv-1', '  Prefere massa al dente  ')
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.ok(post, 'não houve POST')
  assert.match(post.url, new RegExp(`/api/clientes/${CLIENTE}/notas$`))
  assert.deepEqual(post.corpo, { texto: 'Prefere massa al dente' })
  assert.ok(chamadas.some((c) => c.url.endsWith('/conv-1/dossie')), 'não releu o dossiê')
  assert.ok(despachados.some((a) => a.tipo === acao.CLIENTE_DA_API), 'Ficha não atualizou')
})

await confere('lead sem cadastro recebe aviso e não chama a API', async () => {
  const { acoes, chamadas, despachados } = montar({ id: 'conv-2', clienteId: null, cliente: { notas: [] } })
  await acoes.salvarNota('conv-2', 'qualquer')
  assert.equal(chamadas.length, 0)
  const aviso = despachados.find((a) => a.tipo === acao.AVISO_API)
  assert.ok(aviso)
  assert.match(aviso.mensagem, /cadastr/i)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
