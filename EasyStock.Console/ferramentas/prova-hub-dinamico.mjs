import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
  }
  return proximo(spec, contexto)
} })
const { indicadoresDoHall } = await import('../src/dominio/pulsoDoHall.js')
const { normalizarPreferenciasHall, aplicarFocoDoHall, ordenarPaineisDoHall } = await import('../src/dominio/preferenciasHall.js')
const ids = ['atendimento', 'cozinha', 'entregas', 'financeiro']
const antes = { ordem: ['entregas', 'atendimento'], favoritos: ['cozinha'] }
assert.equal(normalizarPreferenciasHall(antes).ordem[0], 'entregas')
assert.deepEqual(antes, { ordem: ['entregas', 'atendimento'], favoritos: ['cozinha'] })
assert.equal(aplicarFocoDoHall(antes, 'atendimento').ordem[0], 'atendimento')
assert.equal(aplicarFocoDoHall(antes, 'producao').ordem[0], 'producao')
assert.deepEqual(aplicarFocoDoHall(antes, 'gestao').favoritos, ['cozinha'])
assert.deepEqual(normalizarPreferenciasHall({ paineis: ['privado', 'cozinha', 'cozinha'], movimentos: false }).paineis, ['cozinha'])
assert.deepEqual(normalizarPreferenciasHall({ paineis: [] }).paineis, [])
assert.equal(normalizarPreferenciasHall({ foco: 'privado' }).foco, 'atendimento')
assert.equal('ritmo' in normalizarPreferenciasHall({ ritmo: true }), false)
for (const [foco, esperado] of [
  ['atendimento', ['atendimento', 'entregas', 'cozinha', 'financeiro']],
  ['gestao', ['financeiro', 'atendimento', 'entregas', 'cozinha']],
  ['producao', ['cozinha', 'entregas', 'atendimento', 'financeiro']],
]) {
  assert.deepEqual(ordenarPaineisDoHall({ foco }, ids), esperado)
  assert.deepEqual(ordenarPaineisDoHall({ foco, paineis: ['cozinha', 'financeiro'] }, ['cozinha', 'atendimento']), ['cozinha'])
  assert.deepEqual(ordenarPaineisDoHall({ foco, paineis: [] }, ids), [])
}
const carregando = indicadoresDoHall({}, ids)
assert.ok(carregando.every((item) => item.valor === null && !item.atual))
const fontes = {
  atendimento: { estado: 'ok', dados: { itens: [
    { situacao: 'Automatica', aguardaResposta: true },
    { situacao: 'Assumida', aguardaResposta: true, assumidaPorUsuarioId: 'outro' },
    { situacao: 'Assumida', aguardaResposta: false },
    { situacao: 'Assumida', aguardaResposta: false, assumidaPorUsuarioId: 'eu' },
    { situacao: 'Encerrada', aguardaResposta: true },
  ], parcial: false } },
  pedidos: { estado: 'ok', dados: { itens: ['aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue'].map((status) => ({ status })), parcial: false } },
  financeiro: { estado: 'ok', dados: { saldoEsperado: 300.03, aberto: true } },
}
assert.deepEqual(indicadoresDoHall(fontes, ids).map((item) => item.valor), [2, 2, 1, 300.03])
fontes.atendimento.estado = 'carregando'
assert.equal(indicadoresDoHall(fontes, ids)[0].valor, 2)
assert.equal(indicadoresDoHall(fontes, ids)[0].atual, false)
fontes.atendimento.estado = 'erro'
assert.equal(indicadoresDoHall(fontes, ids)[0].valor, 2)
assert.equal(indicadoresDoHall(fontes, ids)[0].atual, false)
fontes.atendimento.dados.itens = []
assert.equal(indicadoresDoHall(fontes, ids)[0].valor, 0)
fontes.atendimento.estado = 'ok'; fontes.atendimento.dados.parcial = true
assert.equal(indicadoresDoHall(fontes, ids)[0].parcial, true)
fontes.financeiro.dados = {}
assert.equal(indicadoresDoHall(fontes, ids)[3].valor, null)
console.log('Hub: migração de preferências, modelos, números, recortes, falha e dados parciais conferidos.')
