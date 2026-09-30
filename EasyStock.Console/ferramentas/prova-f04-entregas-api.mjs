// Prova da F04 (issue #1221): as entregas no modo API. Cobre as partes puras:
// a divisão dos pedidos do KDS e das viagens da S44 em painéis (aprovação da
// S12, prontos sem viagem, em rota), a trava de saída sem entregador (RN-32),
// o motivo legível da aprovação (S14) e os corpos do cadastro da S45.
//
// Roda com: node ferramentas/prova-f04-entregas-api.mjs

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

const {
  STATUS_KDS_ENTREGAS, paineisDeEntregas, motivoDeSaida, motivoDaAprovacao, recarregaEntregasCom,
  corpoJanela, corpoZona, corpoBloqueio, rotuloSituacaoViagem,
} = await import('../src/dominio/entregasApi.js')

// A gaveta pede ao KDS os três status que importam para a entrega.
assert.equal(STATUS_KDS_ENTREGAS, 'aguardando_aprovacao_baba,pronto,saiu_para_entrega')

const pedidos = [
  { id: 'a', status: 'aguardando_aprovacao_baba', requerAprovacao: true, motivoRequerAprovacao: 'fora_de_area' },
  { id: 'b', status: 'pronto', endereco: 'Rua A, 1' },
  { id: 'c', status: 'pronto' },
  { id: 'd', status: 'saiu_para_entrega' },
  { id: 'e', status: 'preparando' },
]
const viagens = [
  { id: 'v1', situacao: 'Montando', entregadorId: null, paradas: [{ pedidoId: 'c', ordem: 1 }] },
  { id: 'v2', situacao: 'EmRota', entregadorId: 'x', paradas: [{ pedidoId: 'd', ordem: 1 }] },
  { id: 'v3', situacao: 'Concluida', entregadorId: 'x', paradas: [{ pedidoId: 'z', ordem: 1 }] },
]
const p = paineisDeEntregas(pedidos, viagens)
assert.deepEqual(p.aprovacao.map((x) => x.id), ['a'])
// "c" já está numa viagem montando: não aparece de novo como pronto solto.
assert.deepEqual(p.prontos.map((x) => x.id), ['b'])
assert.deepEqual(p.montando.map((v) => v.id), ['v1'])
assert.deepEqual(p.emRota.map((v) => v.id), ['v2'])
assert.equal(paineisDeEntregas(null, null).prontos.length, 0)

// RN-32: sem entregador ou sem parada, a viagem não sai, e a tela diz por quê.
assert.match(motivoDeSaida({ entregadorId: null, paradas: [{}] }), /entregador/i)
assert.match(motivoDeSaida({ entregadorId: 'x', paradas: [] }), /pedido/i)
assert.equal(motivoDeSaida({ entregadorId: 'x', paradas: [{}] }), null)

assert.equal(motivoDaAprovacao('fora_de_area'), 'Entrega fora da área')
assert.equal(motivoDaAprovacao('outra coisa'), 'outra coisa')
assert.equal(motivoDaAprovacao(null), 'Aprovação manual')

assert.equal(rotuloSituacaoViagem('Montando'), 'Montando')
assert.equal(rotuloSituacaoViagem('EmRota'), 'Em rota')

// Recarrega com o `ready` e com qualquer evento de pedido do SSE da S18.
assert.equal(recarregaEntregasCom('ready'), true)
assert.equal(recarregaEntregasCom('pedido.mudou_status'), true)
assert.equal(recarregaEntregasCom('conversa.nova'), false)

// S45: corpos que a API aceita (TimeOnly "HH:mm:ss", CEP só dígitos, bairros em lista).
assert.deepEqual(
  corpoJanela({ diaDaSemana: '5', horaInicio: '18:00', horaFim: '20:30', capacidadeMaxima: '12', label: ' Sexta noite ' }),
  { diaDaSemana: 5, horaInicio: '18:00:00', horaFim: '20:30:00', capacidadeMaxima: 12, label: 'Sexta noite' },
)
assert.deepEqual(
  corpoZona({ label: 'Centro', valor: '7,50', tempoEstimadoMinutos: '40', ordem: '1', cobertura: 'cep', cepInicio: '24000-000', cepFim: '24099-999', bairros: '' }),
  { label: 'Centro', valor: 7.5, tempoEstimadoMinutos: 40, ordem: 1, cepInicio: '24000000', cepFim: '24099999', bairros: null },
)
assert.deepEqual(
  corpoZona({ label: 'Icaraí', valor: '10', tempoEstimadoMinutos: '50', ordem: '2', cobertura: 'bairros', cepInicio: '1', cepFim: '2', bairros: 'Icaraí, Ingá ,, ' }),
  { label: 'Icaraí', valor: 10, tempoEstimadoMinutos: 50, ordem: 2, cepInicio: null, cepFim: null, bairros: ['Icaraí', 'Ingá'] },
)
assert.deepEqual(
  corpoBloqueio({ data: '2026-12-25', motivo: ' Natal ', janelaEspecificaId: '' }),
  { data: '2026-12-25', motivo: 'Natal', janelaEspecificaId: null },
)

console.log('prova F04 (entregas na API): ok')
