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

// No modo API a ocorrência não vem no pedido: o cadastro do cliente traz só a contagem das abertas (revisão da PR).
caso('modo API: ocorrência aberta no cadastro do cliente segura a despedida', () => {
  assert.equal(despedirAoEncerrar({ ...semPendencia, cliente: { ocorrenciasAbertas: 1 } }, { regra: REGRA, podeDespedir: true }), false)
  assert.equal(despedirAoEncerrar({ ...semPendencia, cliente: { ocorrenciasAbertas: 0 } }, { regra: REGRA, podeDespedir: true }), true)
  assert.equal(despedirAoEncerrar({ ...semPendencia, cliente: {} }, { regra: REGRA, podeDespedir: true }), true)
})

caso('sem regra ou sem permissão de escrita, a despedida continua desmarcada', () => {
  assert.equal(despedirAoEncerrar(semPendencia, { regra: null, podeDespedir: true }), false)
  assert.equal(despedirAoEncerrar(semPendencia, { regra: REGRA, podeDespedir: false }), false)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
