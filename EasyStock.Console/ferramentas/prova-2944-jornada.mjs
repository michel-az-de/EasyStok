// #2944 Bloco C (E1-17 a E1-21): cancelar pedido em Pedidos, voltar do navegador, registrar atualiza, Cozinha, cliente sem celular.
//   node ferramentas/prova-2944-jornada.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const pc = await import('../src/dominio/pedidosCadastro.js')
const painel = fonte('features/gestao/cadastros/PainelPedidos.jsx')

await caso('17a. elegibilidade: entregue e cancelado dizem por que não cancelam; o resto cancela', () => {
  assert.equal(typeof pc.motivoParaNaoCancelar, 'function', 'motivoParaNaoCancelar ainda não existe')
  assert.equal(pc.motivoParaNaoCancelar('entregue'), 'Já foi entregue')
  assert.equal(pc.motivoParaNaoCancelar('cancelado'), 'Já foi cancelado')
  for (const s of ['aguardando_pagamento', 'aguardando', 'aprovado_baba', 'preparando', 'pronto', 'saiu_para_entrega', 'rascunho']) assert.equal(pc.motivoParaNaoCancelar(s), null, s)
})
await caso('17b. a linha oferece "Cancelar pedido" com motivo de bloqueio, confirmação e recarga da lista e da Ficha', () => {
  assert.match(painel, /Cancelar pedido/)
  assert.match(painel, /motivo=\{motivoParaNaoCancelar\(item\.status\)\}/)
  assert.match(painel, /cancelarPedido\(/)
  assert.match(painel, /Manter pedido/)
  assert.match(painel, /consulta\.recarregar\(\)/)
  assert.match(painel, /easystok:sincronizar-agora/)
})
fim()
