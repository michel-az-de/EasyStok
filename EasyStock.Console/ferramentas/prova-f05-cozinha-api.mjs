// Prova da F05 (issue #1218): a cozinha no modo API. Cobre as partes puras:
// o próximo passo de cada status do KDS (S19), o aviso de início previsto e
// atraso (S21) e o leitor de quadros do SSE de operação (S18), que recebe o
// texto em pedaços do `fetch` e devolve os eventos nomeados inteiros.
//
// Roda com: node ferramentas/prova-f05-cozinha-api.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { COLUNAS_KDS, proximoStatusKds, avisoDeInicio } = await import('../src/dominio/kds.js')
const { criarLeitorSse } = await import('../src/infra/api/leitorSse.js')

// Esteira do KDS sobre Pedido: aguardando -> preparando -> pronto -> saiu_para_entrega -> entregue.
assert.deepEqual(COLUNAS_KDS.map((c) => c.status), ['aguardando', 'preparando', 'pronto', 'saiu_para_entrega'])
assert.equal(proximoStatusKds('aguardando').status, 'preparando')
assert.equal(proximoStatusKds('preparando').status, 'pronto')
assert.equal(proximoStatusKds('pronto').status, 'saiu_para_entrega')
assert.equal(proximoStatusKds('saiu_para_entrega').status, 'entregue')
assert.equal(proximoStatusKds('entregue'), null)
assert.equal(proximoStatusKds('cancelado'), null)

// S21: atrasado vence; sem início previsto e sem atraso, nada a dizer.
const agora = Date.parse('2026-09-30T12:00:00Z')
assert.equal(avisoDeInicio({ inicioPrevistoEm: null, atrasado: false }, agora), null)
assert.equal(avisoDeInicio({ inicioPrevistoEm: '2026-09-30T11:40:00Z', atrasado: true }, agora).atrasado, true)
assert.match(avisoDeInicio({ inicioPrevistoEm: '2026-09-30T11:40:00Z', atrasado: true }, agora).texto, /20 min/)
assert.equal(avisoDeInicio({ inicioPrevistoEm: '2026-09-30T12:30:00Z', atrasado: false }, agora).atrasado, false)
assert.equal(avisoDeInicio({ inicioPrevistoEm: null, atrasado: true }, agora).atrasado, true)

// SSE: quadro partido em dois pedaços, heartbeat ignorado, CRLF aceito.
const eventos = []
const ler = criarLeitorSse((e) => eventos.push(e))
ler('event: ready\ndata: {}\n\n: heartbeat\n\nevent: pedido.pa')
ler('go\ndata: {"pedidoId":"a1"}\n\r\n')
ler('event: pedido.mudou_status\r\ndata: {"pedidoId":"b2","statusNovo":"pronto"}\r\n\r\n')
assert.deepEqual(eventos, [
  { evento: 'ready', dados: {} },
  { evento: 'pedido.pago', dados: { pedidoId: 'a1' } },
  { evento: 'pedido.mudou_status', dados: { pedidoId: 'b2', statusNovo: 'pronto' } },
])

console.log('prova F05 (cozinha na API): ok')
