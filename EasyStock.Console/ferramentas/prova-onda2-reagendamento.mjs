import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
registerHooks({
  resolve(id, contexto, proximo) {
    if (id.startsWith('.') && !/\.[a-z]+$/i.test(id)) {
      try { return proximo(id + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(id, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const pedidoId = '11111111-1111-1111-1111-111111111111'
const janelaId = '22222222-2222-2222-2222-222222222222'
const janela = { janelaId, data: '2026-10-14', label: 'Almoço', horaInicio: '12:00:00', horaFim: '13:00:00' }
const json = (data, status = 200) => new Response(JSON.stringify(data), { status })
const despachos = [], chamadas = []
const estadoRef = { current: { conversas: [{ id: 'conversa', pedido: { pedidoId, janela: 'antiga', estado: 'pago' } }] } }
const acoes = criarAcoesComandaApi({}, { despachar: a => despachos.push(a), estadoRef })
let liberar
const espera = new Promise(resolve => { liberar = resolve })
const respostaPedido = { pedidoId, status: 'aguardando', total: 25, frete: 0, itens: [], totalPago: 10, janela }
globalThis.fetch = async (url, opcoes) => {
  chamadas.push({ url, ...opcoes })
  if (opcoes.method === 'PATCH') { await espera; return json({ data: { alterado: true, janela, avisoEnfileirado: false } }) }
  return json({ data: respostaPedido })
}
const primeira = acoes.escolherJanela('conversa', `${janelaId}|${janela.data}`, false)
const segunda = acoes.escolherJanela('conversa', `${janelaId}|${janela.data}`, false)
liberar()
await Promise.all([primeira, segunda])
assert.equal(chamadas.filter(c => c.method === 'PATCH').length, 1)
assert.equal(chamadas[0].url, `/api/pedidos/${pedidoId}/janela`)
assert.deepEqual(JSON.parse(chamadas[0].body), { janelaId, data: janela.data, avisarCliente: false })
assert.equal(despachos.at(-1).pedido.janela, `${janelaId}|${janela.data}`)
assert.equal(despachos.at(-1).pedido.totalPagoApi, 10)
const anterior = estadoInicial({ conversas: estadoRef.current.conversas, catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })
assert.equal(reducer(anterior, despachos.at(-1)).conversas[0].pedido.janela, `${janelaId}|${janela.data}`, 'vaga do servidor vence a antiga')
assert.equal(pedidoDaApi(respostaPedido).janela, `${janelaId}|${janela.data}`, 'recarga sem estado local conserva a vaga')
for (const status of [400, 403, 409, 503]) {
  despachos.length = 0
  globalThis.fetch = async () => json({ error: { message: 'Troca recusada' } }, status)
  await assert.rejects(acoes.escolherJanela('conversa', `${janelaId}|${janela.data}`, true), /Troca recusada/)
  assert.ok(despachos.every(a => a.tipo !== acao.SINCRONIZAR_PEDIDO))
}
globalThis.fetch = async (_url, opcoes) => opcoes.method === 'PATCH'
  ? json({ data: { alterado: true, janela, avisoEnfileirado: true } }) : json({ error: { message: 'Falha de consulta' } }, 503)
assert.equal((await acoes.escolherJanela('conversa', `${janelaId}|${janela.data}`, true)).alterado, true)
assert.match(despachos.at(-1).mensagem, /Agendamento alterado.*atualizar/)
console.log('Reagendamento: vaga persistida, recarga, pagamento preservado, duplo clique, recusas e falha de consulta aprovados.')
