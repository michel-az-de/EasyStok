/* eslint-disable no-console */
// Prova da #3339 (R1D-01, `docs/dev/anexos-estresse-atendimento-2026-11-25/r1-devolucao-reclamacao.md`): Concluir
// marcava "Mandar mensagem de encerramento" para quem ainda espera estorno. Otávio pediu R$ 85,00 de volta,
// o estorno não estava marcado, e o clique em Concluir mandou "Por hoje é só". Agora a caixa nasce desmarcada
// enquanto houver estorno em aberto ou ocorrência aberta, e o modal diz qual estorno falta.
//
//   node ferramentas/prova-3339-encerramento.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const dominio = await import('../src/dominio/resumoAtendimento.js')
const { despedirAoEncerrar, avisoDoEstornoAoEncerrar } = dominio

const REGRA = { ativa: true, texto: 'Por hoje é só' }

// Caso do R1D-01: pedido pago, estorno de R$ 85,00 pedido na reclamação e ainda não marcado.
const otavio = {
  pedido: {
    numero: 41,
    estado: 'pronto',
    cobranca: { pagaEm: 1, valorPago: 85, estornadaEm: null },
    ocorrencia: { estado: 'em_apuracao', encerradaEm: null, estorno: null, historico: [{ autor: 'cliente', texto: 'Quero o dinheiro de volta' }] },
  },
}

// Sem pendência: pedido pago sem reclamação, e pedido com estorno já marcado.
const semPendencia = { pedido: { numero: 42, estado: 'pronto', cobranca: { pagaEm: 1, valorPago: 60, estornadaEm: null }, ocorrencia: null } }
const estornado = {
  pedido: {
    numero: 43,
    estado: 'pronto',
    cobranca: { pagaEm: 1, valorPago: 85, estornadaEm: 2, valorEstornado: 85 },
    ocorrencia: { estado: 'encerrada_com_estorno', encerradaEm: 2, estorno: { motivo: 'lasanha virada', valor: 85, em: 2 }, historico: [] },
  },
}

caso('R1D-01: a despedida nasce desmarcada para quem espera estorno', () => {
  assert.equal(typeof despedirAoEncerrar, 'function', 'despedirAoEncerrar não existe')
  assert.equal(despedirAoEncerrar(otavio, { regra: REGRA, podeDespedir: true }), false)
})

caso('R1D-01: a ocorrência aberta segura a despedida mesmo sem valor a estornar', () => {
  const aberta = { pedido: { numero: 44, estado: 'pronto', cobranca: null, ocorrencia: { estado: 'aberta', encerradaEm: null, estorno: null, historico: [] } } }
  assert.equal(despedirAoEncerrar(aberta, { regra: REGRA, podeDespedir: true }), false)
})

caso('R1D-01: o modal mostra a linha do estorno não marcado', () => {
  assert.equal(typeof avisoDoEstornoAoEncerrar, 'function', 'avisoDoEstornoAoEncerrar não existe')
  assert.match(avisoDoEstornoAoEncerrar(otavio), /^Estorno de R\$\s85,00 não marcado$/)
})

caso('sem pendência, a despedida segue marcada pela regra', () => {
  assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: true }), true)
  assert.equal(avisoDoEstornoAoEncerrar(semPendencia), null)
})

caso('estorno já marcado não segura a despedida nem gera linha', () => {
  assert.equal(despedirAoEncerrar(estornado, { regra: REGRA, podeDespedir: true }), true)
  assert.equal(avisoDoEstornoAoEncerrar(estornado), null)
})

// No modo API a conversa não traz a ocorrência: o modal lê as do pedido em `GET /api/ocorrencias?pedidoId=` e passa a lista
// (formato do OcorrenciaDto). Sem a lista (ainda carregando, ou a leitura falhou), vale o que a conversa já tem.
const doPedido = (extra = {}) => ({ id: 'o1', status: 'aberta', reembolsoValor: null, reembolsoEm: null, reembolsoSituacao: null, ...extra })

caso('modo API: ocorrência aberta do pedido segura a despedida', () => {
  const abertas = [doPedido()]
  assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: true, ocorrenciasDoPedido: abertas }), false)
})

caso('modo API: só ocorrência encerrada, lista vazia ou ausente não seguram a despedida', () => {
  for (const lista of [[doPedido({ status: 'resolvida' })], [], null, undefined]) {
    assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: true, ocorrenciasDoPedido: lista }), true)
  }
})

caso('modo API: o modal diz o estorno que a ocorrência aberta ainda não concluiu', () => {
  const pedido = [doPedido({ reembolsoValor: 85, reembolsoSolicitadoEm: '2026-11-25T12:00:00Z' })]
  assert.match(avisoDoEstornoAoEncerrar(semPendencia, pedido), /^Estorno de R\$\s85,00 não marcado$/)
})

// Revisão da PR: o estorno online confirmado pelo provedor chega com `reembolsoSituacao: 'confirmado'` e `reembolsoEm`
// ainda nulo enquanto a ocorrência segue aberta; dizer "não marcado" levaria a devolver o dinheiro duas vezes.
caso('modo API: lista ainda não lida ou leitura que falhou não deixa a despedida nascer marcada', () => {
  assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: true, ocorrenciasIncertas: true }), false)
  assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: true, ocorrenciasIncertas: false, ocorrenciasDoPedido: [] }), true)
})

caso('modo API: estorno confirmado, recusado ou sem valor não gera a linha', () => {
  for (const o of [doPedido({ reembolsoValor: 85, reembolsoEm: '2026-11-25T13:00:00Z' }), doPedido({ reembolsoValor: 85, reembolsoSituacao: 'recusado' }), doPedido({ reembolsoValor: 85, reembolsoSituacao: 'confirmado' }), doPedido(), doPedido({ status: 'resolvida', reembolsoValor: 85 })]) {
    assert.equal(avisoDoEstornoAoEncerrar(semPendencia, [o]), null)
  }
})

caso('sem regra ou sem permissão de escrita, a despedida continua desmarcada', () => {
  assert.equal(despedirAoEncerrar(semPendencia, { regra: null, podeDespedir: true }), false)
  assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: false }), false)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
