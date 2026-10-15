// #1863: o lote da fila offline recusa por item o "entregue" com saldo a receber; a tela lê quem parou.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(s, c, next) { return next(s.startsWith('.') && !/\.[a-z]+$/i.test(s) ? s + '.js' : s, c) } })
const { itensDoLoteComSaldoAReceber } = await import('../src/dominio/pagamento.js')
const resultados = [
  { id: 'a', sucesso: false, status: null, erro: 'Falta receber R$ 25,00 deste pedido.', codigo: 'SALDO_A_RECEBER', valor: 25 },
  { id: 'b', sucesso: true, status: 'preparando', erro: null, codigo: null, valor: null },
  { id: 'c', sucesso: false, status: null, erro: 'Transição inválida.', codigo: null, valor: null },
]
assert.deepEqual(itensDoLoteComSaldoAReceber(resultados), [{ id: 'a', valor: 25, mensagem: 'Falta receber R$ 25,00 deste pedido.' }])
assert.deepEqual(itensDoLoteComSaldoAReceber([]), [])
assert.deepEqual(itensDoLoteComSaldoAReceber(), [])
console.log('3 cenários do lote com saldo a receber aprovados.')
