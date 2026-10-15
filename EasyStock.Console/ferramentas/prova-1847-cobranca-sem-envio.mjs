// #1847: ações reais e contrato HTTP controlado, sem criar pedido nem enviar mensagens reais.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { situacaoDoEnvio } = await import('../src/dominio/resumoPedido.js')
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
for (const enviarAoCliente of [false, true]) {
  const pedidos = [], despachados = []
  const cobranca = { cobrancaId: 'cb', provedor: 'mercadopago', status: 'Pendente', valor: 50, tentativa: 1, linkPagamento: 'https://mp.test/link', criadaEm: '2026-10-15T15:00:00Z', expiraEm: '2026-10-15T15:30:00Z' }
  globalThis.fetch = async (url, opcoes = {}) => {
    if (opcoes.method === 'POST') { pedidos.push(JSON.parse(opcoes.body)); return new Response(JSON.stringify({ data: { pedidoId: 'pedido', cobranca, enviadoAoCliente: enviarAoCliente } })) }
    return new Response(JSON.stringify({ data: { pedidoId: 'pedido', status: 'aguardando_pagamento', total: 50, frete: 0, itens: [], totalPago: 0, pagamentos: [], cobranca } }))
  }
  const pedido = { estado: 'aguardando', itens: [{ sku: 'massa', qtd: 1, obs: '' }], janela: 'j1|2026-10-16', meio: 'pix' }
  const acoes = criarAcoesComandaApi({}, { despachar: a => despachados.push(a), estadoRef: { current: { conversas: [{ id: 'conversa', pedido }] } } })
  if (enviarAoCliente) await acoes.gerarPedido('conversa', 'pix')
  else await acoes.gerarCobranca('conversa', pedido, 'pix', false)
  assert.equal(pedidos.length, 1)
  assert.equal(pedidos[0].enviarAoCliente, enviarAoCliente, 'o default mantém envio; a janela fechada usa false explícito')
  const sincronizado = despachados.findLast(a => a.pedido)?.pedido
  assert.ok(sincronizado?.cobranca?.link, 'o link fica disponível para copiar')
  if (!enviarAoCliente) {
    assert.equal(situacaoDoEnvio(sincronizado).chave, 'nao-enviada', 'não diz que enviou ao cliente')
    assert.ok(despachados.some(a => /criados sem envio/.test(a.mensagem ?? '')))
    assert.ok(!despachados.some(a => /mas|não saiu/.test(a.mensagem ?? '')), 'geração intencional não exibe falsa falha')
  }
}
console.log('2 cenários de geração com e sem envio aprovados.')
