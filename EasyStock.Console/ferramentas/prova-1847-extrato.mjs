// #1847: cancelamento encerra cobrança; saldo agregado não identifica estorno ou cupom.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(s, c, next) { return next(s.startsWith('.') && !/\.[a-z]+$/i.test(s) ? s + '.js' : s, c) } })
const { extratoDoPedido } = await import('../src/dominio/pagamento.js')
const casos = [
  ['cancelado sem pagamento', 'cancelado', 0, 0, 0, 0],
  ['cancelado após estorno integral', 'cancelado', 100, 0, 0, -100],
  ['cancelado após estorno parcial', 'cancelado', 100, 50, 0, -50],
  ['cancelado com crédito convertido sem reembolso', 'cancelado', 100, 50, 0, -50],
  ['ativo parcialmente pago', 'aguardando', 50, 50, 50, 0],
  ['ativo quitado', 'pago', 100, 100, 0, 0],
  ['ativo com cupom aplicado', 'aguardando', 50, 75, 25, 25],
  ['ativo com crédito convertido sem reembolso', 'aguardando', 100, 50, 50, -50],
]
for (const [nome, estado, recebido, alocado, saldo, ajuste] of casos) {
  const extrato = extratoDoPedido({ pedidoId: 'pedido', estado, totalApi: 100, frete: 10, totalPagoApi: recebido, alocadoApi: alocado,
    pagamentosApi: recebido ? [{ pagamentoId: 'p', valor: recebido, em: 1, meio: 'pix' }] : [] })
  assert.equal(extrato.saldo, saldo, nome)
  assert.equal(extrato.ajuste, ajuste, nome)
  assert.equal(extrato.pago, alocado, 'preserva o saldo de pagamentos e créditos')
  assert.equal(extrato.total, 100, 'preserva o valor original')
  assert.equal(extrato.pagamentos.reduce((s, p) => s + p.valor, 0), recebido, 'preserva o histórico recebido')
  assert.equal(extrato.cancelado, estado === 'cancelado')
}
console.log(`${casos.length} cenários de extrato aprovados.`)
