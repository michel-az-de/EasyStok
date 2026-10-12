/* eslint-disable no-console */
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(s, c, next) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) {
      try { return next(s + '.js', c) } catch { /* resolução original */ }
    }
    return next(s, c)
  },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return next(url, c)
  },
})
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { janelaDoId, corpoDoPedido } = await import('../src/infra/api/comandaApi.js')
const { lerLembretesDaApi } = await import('../src/aplicacao/api/lembretes.js')
const { ORIGENS } = await import('../src/dominio/lembrete.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { SINCRONIZAR_LEMBRETES_API } = await import('../src/aplicacao/acoes.js')
const chamadas = []
const janelaId = '22222222-2222-2222-2222-222222222222'
const data = '2026-10-20'
globalThis.fetch = async (url, opcoes) => {
  chamadas.push({ url, ...opcoes })
  return new Response(JSON.stringify({ data: { lojaDisponivel: true,
    janelas: [{ janelaId, data, horaInicio: '13:00:00', horaFim: '14:00:00', label: 'Almoço', vagasRestantes: 3, capacidade: 5 }] } }), { status: 200 })
}
const acoes = criarAcoesComandaApi({}, { despachar: () => {}, estadoRef: { current: {} } })
const oferta = await acoes.carregarJanelasComanda([janelaId], data)
const busca = new URL(chamadas[0].url, 'http://local').searchParams
assert.equal(busca.get('dataInicio'), data)
assert.equal(busca.get('dataFim'), data)
assert.equal(busca.get('itens'), janelaId)
assert.deepEqual(janelaDoId(oferta.janelas[0].id), { janelaId, data })
assert.equal(corpoDoPedido({ itens: [], janela: oferta.janelas[0].id }).dataEntrega, data)
await acoes.carregarJanelasComanda([])
assert.equal(new URL(chamadas[1].url, 'http://local').searchParams.has('dataInicio'), false)
globalThis.fetch = async () => new Response(JSON.stringify({ error: { message: 'Data recusada' } }), { status: 400 })
await assert.rejects(acoes.carregarJanelasComanda([], data), /Data recusada/)

const dto = { id: 'agendado', tipo: 'PedidoAgendado', texto: 'Encomenda amanhã', venceEm: '2026-10-10T11:00:00Z', conversaId: 'conversa', pedidoId: 'pedido' }
globalThis.fetch = async () => new Response(JSON.stringify({ data: [dto] }), { status: 200 })
const lembretes = await lerLembretesDaApi()
assert.equal(lembretes[0].origem, ORIGENS.AGENDAMENTO)
assert.equal(lembretes[0].conversaId, dto.conversaId)
assert.equal(lembretes[0].titulo, dto.texto)
assert.equal(lembretes[0].quando, Date.parse(dto.venceEm))
let estado = estadoInicial({ conversas: [], catalogo: {}, regras: [] })
estado = reducer(estado, { tipo: SINCRONIZAR_LEMBRETES_API, lembretes, revisao: 0 })
estado = reducer(estado, { tipo: SINCRONIZAR_LEMBRETES_API, lembretes, revisao: 0 })
assert.equal(estado.lembretes.length, 1, 'sincronizar o aviso duas vezes não duplica')
estado = reducer(estado, { tipo: SINCRONIZAR_LEMBRETES_API, lembretes: [], revisao: 0 })
assert.equal(estado.lembretes.length, 0, 'a resolução no servidor retira o aviso do painel')
console.log('M3.8: data da encomenda, reserva, recusa, tipo automático e sincronização sem duplicação aprovados.')
