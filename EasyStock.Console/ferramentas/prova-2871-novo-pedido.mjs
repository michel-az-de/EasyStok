// #2871: "Novo pedido" do cliente que pediu por outro canal. (1) O anúncio de abertura leva a intenção `novoPedido`,
// também para quem monta depois (a tela de Cadastros troca o hash e o Balcão monta em seguida). (2) Fora da janela de
// 24 h o resumo vai pela fila de avisos: a comanda não diz que o resumo não saiu. Ações reais, HTTP controlado.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const { criarCanalDeAbertura } = await import('../src/aplicacao/aberturaDeConversa.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { situacaoDoEnvio } = await import('../src/dominio/resumoPedido.js')

// 1. A intenção chega a quem já ouve e a quem monta depois; a abertura comum segue sem intenção.
{
  const canal = criarCanalDeAbertura(new EventTarget())
  canal.anunciar('c1', { novoPedido: true })
  const tardio = []
  const sair = canal.inscrever((id, detalhe) => tardio.push([id, detalhe.novoPedido === true]))
  assert.deepEqual(tardio, [['c1', true]], 'quem monta depois recebe a intenção pendente')
  canal.anunciar('c2')
  assert.deepEqual(tardio.at(-1), ['c2', false], 'abertura comum não abre o cardápio')
  canal.anunciar('c3', { novoPedido: true, id: 'outro' })
  assert.deepEqual(tardio.at(-1), ['c3', true], 'a intenção não troca a conversa anunciada')
  sair()
}

// 2. Resumo pela fila (fora da janela) e resumo que não saiu (canal fora): avisos e marca diferentes.
const identidade = { clienteId: 'cliente-da-comanda', vinculoRevisao: 2 }
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const cobranca = { cobrancaId: 'cb', provedor: 'mercadopago', status: 'Pendente', valor: 50, tentativa: 1, linkPagamento: 'https://mp.test/link', criadaEm: '2026-11-19T15:00:00Z', expiraEm: '2026-11-19T15:30:00Z' }
async function gerar(resposta) {
  const despachados = []
  globalThis.fetch = async (_url, opcoes = {}) => {
    if (opcoes.method === 'POST') return new Response(JSON.stringify({ data: { pedidoId: 'pedido', cobranca, ...resposta } }))
    return new Response(JSON.stringify({ data: { pedidoId: 'pedido', status: 'aguardando_pagamento', total: 50, frete: 0, itens: [], totalPago: 0, pagamentos: [], cobranca } }))
  }
  const pedido = { estado: 'aguardando', identidadeProposta: { ...identidade }, itens: [{ sku: 'massa', qtd: 1, obs: '' }], janela: 'j1|2026-11-20', meio: 'pix' }
  const acoes = criarAcoesComandaApi({}, { despachar: a => despachados.push(a), estadoRef: { current: { conversas: [{ id: 'conversa', ...identidade, pedido }] } } })
  await acoes.gerarPedido('conversa', 'pix')
  const avisos = despachados.map(a => a.mensagem).filter(Boolean)
  return { avisos, sincronizado: despachados.findLast(a => a.pedido)?.pedido }
}
{
  const { avisos, sincronizado } = await gerar({ enviadoAoCliente: false, resumoNaFila: true })
  assert.ok(avisos.some(m => /Pedido criado\. O resumo sai pelo WhatsApp em instantes/.test(m)), `aviso da fila: ${avisos}`)
  assert.ok(!avisos.some(m => /não saiu/.test(m)), 'resumo na fila não é falha de envio')
  assert.notEqual(situacaoDoEnvio(sincronizado).chave, 'nao-enviada', 'o pedido não fica marcado como resumo não enviado')
}
{
  const { avisos, sincronizado } = await gerar({ enviadoAoCliente: false })
  assert.ok(avisos.some(m => /o resumo não saiu ao cliente/.test(m)), 'sem a fila o aviso de falha continua')
  assert.equal(situacaoDoEnvio(sincronizado).chave, 'nao-enviada')
}
console.log('ok prova-2871: intenção de novo pedido no anúncio e resumo pela fila fora da janela')
