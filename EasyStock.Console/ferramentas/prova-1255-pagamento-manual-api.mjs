import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { totalPago } = await import('../src/dominio/pagamento.js')
const { situacaoDaCobranca, podeDesfazerPagamento } = await import('../src/dominio/cobranca.js')
const agora = '2026-10-06T15:00:00Z'
const dados = { pedidoId: '11111111-1111-1111-1111-111111111111', status: 'aguardando', total: 25, frete: 5,
  itens: [], totalPago: 25, pagamentos: [{ id: 'pagamento', valor: 25, metodo: 'dinheiro', pagoEm: agora }],
  cobranca: { cobrancaId: 'cobranca', provedor: 'na_entrega', status: 'Paga', valor: 25, valorPago: 25,
    criadaEm: agora, pagaEm: agora, metodoPagamento: 'dinheiro' } }
const pedido = pedidoDaApi(dados)
assert.equal(totalPago(pedido), 25, 'não soma cobrança com o mesmo lançamento manual')
assert.equal(situacaoDaCobranca(pedido.cobranca, Date.now()).chave, 'paga', 'manual não é conciliação bancária')
assert.equal(podeDesfazerPagamento(pedido), true)
assert.equal(podeDesfazerPagamento({ ...pedido, statusApi: 'preparando' }), false)
const parcial = pedidoDaApi({ ...dados, totalPago: 10, pagamentos: [{ ...dados.pagamentos[0], valor: 10 }],
  cobranca: { ...dados.cobranca, status: 'Pendente', pagaEm: null, valorPago: null } })
assert.equal(totalPago(parcial), 10)
assert.equal(podeDesfazerPagamento(parcial), true)
const chamadas = [], despachados = []
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now()+60000, empresa: { id: 'empresa' } }) }
let liberar
const espera = new Promise(resolve => { liberar = resolve })
globalThis.fetch = async (url, opcoes) => {
  chamadas.push({ url, ...opcoes })
  if (opcoes.method === 'POST') await espera
  return new Response(JSON.stringify({ data: dados }))
}
const acoes = criarAcoesComandaApi({}, { despachar: a => despachados.push(a), estadoRef: { current: { conversas: [{ id: 'conversa', pedido }] } } })
const primeiro = acoes.confirmarPagamento('conversa', 25, 'dinheiro')
const repetido = acoes.confirmarPagamento('conversa', 25, 'dinheiro')
liberar()
await Promise.all([primeiro, repetido])
assert.equal(chamadas.filter(c => c.method === 'POST').length, 1, 'duplo clique não repete o POST')
assert.deepEqual(JSON.parse(chamadas[0].body), { empresaId: 'empresa', pedidoId: dados.pedidoId, valor: 25, metodo: 'dinheiro' })
assert.equal(despachados.at(-1).pedido.totalPagoApi, 25, 'saldo vem da recarga da API')
await acoes.desfazerPagamento('conversa', 'Lançamento incorreto')
assert.ok(chamadas.some(c => c.url.endsWith('/pagamento-manual/desfazer') && JSON.parse(c.body).motivo === 'Lançamento incorreto'))
despachados.length = 0
globalThis.fetch = async () => new Response(JSON.stringify({ error: { message: 'Falha ao gravar' } }), { status: 500 })
await acoes.confirmarPagamento('conversa', 25, 'dinheiro')
assert.equal(despachados.length, 1)
assert.match(despachados[0].mensagem, /Pagamento não registrado/)
console.log('Pagamento manual: saldo, conciliação, desfazer, duplo clique e erro de persistência verificados.')
