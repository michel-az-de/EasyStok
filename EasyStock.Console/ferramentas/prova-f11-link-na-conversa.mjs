/* eslint-disable no-console */
// Prova da #1241 (F11, S48): no modo API o pedido feito pelo cardápio do site aparece na conversa.
// O EasyStok grava o pedido na conversa (pedidoEmAndamentoId) e uma mensagem do Sistema com o
// resumo; a sincronização relê o pedido novo e mostra a mensagem como automática.
//
//   node ferramentas/prova-f11-link-na-conversa.mjs

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

const { deveRelerPedido } = await import('../src/aplicacao/planoDeSincronizacao.js')
const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')

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

await confere('conversa que ganhou pedido pela página relê o pedido', () => {
  const resumo = { id: 'c1', pedidoEmAndamentoId: 'p-9', ultimaMensagemEm: '2026-10-08T15:00:00Z' }
  assert.equal(deveRelerPedido(resumo, undefined), true, 'pedido novo, sem cache')
  const outroPedidoAntes = { pedidoId: 'p-1', ultima: '2026-10-08T14:00:00Z', dados: { status: 'entregue' } }
  assert.equal(deveRelerPedido(resumo, outroPedidoAntes), true, 'troca de pedido na conversa')
})

await confere('o resumo do pedido pela página entra como mensagem automática do sistema', () => {
  const m = mensagemDaApi({
    id: 'm1', direcao: 'Saida', autor: 'Sistema', texto: 'Pedido #K7 pela página: 2x Lasanha. Total R$ 90,00',
    enviadaEm: '2026-10-08T15:00:00Z', status: 'Enviada',
  }, 'c1')
  assert.equal(m.dir, 'out')
  assert.equal(m.automatica, true)
  assert.equal(m.origemAutomatica, 'sistema')
  assert.match(m.texto, /pela página/)
})

await confere('o pedido lido vira o pedido da conversa', () => {
  const pedido = pedidoDaApi({ pedidoId: 'p-9', status: 'aguardando_pagamento', itens: [] })
  assert.equal(pedido.pedidoId, 'p-9')
  assert.equal(pedido.estado, 'aguardando')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
