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
const { acaoDisponivel } = await import('../src/aplicacao/api/naoLigadas.js')
const acao = await import('../src/aplicacao/acoes.js')
assert.equal(acaoDisponivel('cancelarPedido', { fonteApi: true }), true, 'cancelamento precisa estar ligado')
const despachos = [], chamadas = []
const pedidoId = '11111111-1111-1111-1111-111111111111'
const estadoRef = { current: { conversas: [{ id: 'conversa', pedido: { pedidoId, estado: 'pago' } }] } }
const acoes = criarAcoesComandaApi({}, { despachar: a => despachos.push(a), estadoRef })
let liberar
const espera = new Promise(resolve => { liberar = resolve })
const json = (dados, status = 200) => new Response(JSON.stringify(dados), { status })
globalThis.fetch = async (url, opcoes) => {
  chamadas.push({ url, ...opcoes })
  if (opcoes.method === 'POST') { await espera; return json({ data: { status: 'cancelado' } }) }
  return json({ data: { pedidoId, status: 'cancelado', total: 25, frete: 0, itens: [], totalPago: 0 } })
}
const primeira = acoes.cancelarPedido('conversa', 'Cliente desistiu')
const segunda = acoes.cancelarPedido('conversa', 'Cliente desistiu')
liberar()
await Promise.all([primeira, segunda])
assert.equal(chamadas.filter(c => c.method === 'POST').length, 1)
assert.equal(chamadas[0].url, `/api/pedidos/${pedidoId}/cancelar`)
assert.equal(JSON.parse(chamadas[0].body).motivo, 'Cliente desistiu')
assert.equal(despachos.at(-1).pedido.estado, 'cancelado')
for (const status of [403, 503]) {
  despachos.length = 0
  globalThis.fetch = async () => json({ error: { message: 'Cancelamento recusado' } }, status)
  const resultado = await acoes.cancelarPedido('conversa', 'Cliente desistiu')
  assert.match(resultado.erro, /Cancelamento recusado/)
  assert.ok(despachos.every(a => a.tipo !== acao.SINCRONIZAR_PEDIDO))
}
despachos.length = 0
globalThis.fetch = async (_url, opcoes) => opcoes.method === 'POST'
  ? json({ data: { status: 'cancelado' } }) : json({ error: { message: 'Falha de consulta' } }, 503)
assert.equal((await acoes.cancelarPedido('conversa', 'Cliente desistiu')).ok, true, 'falha ao reler não desfaz cancelamento confirmado')
assert.match(despachos.at(-1).mensagem, /cancelado.*atualizar/i)
console.log('Cancelamento: persistência, duplo clique, recusa, falha e confirmação com recarga indisponível aprovados.')
