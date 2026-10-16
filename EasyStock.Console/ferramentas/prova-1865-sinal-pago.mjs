// #1865: com só o sinal recebido a Ficha não diz "Pago"; e a dica da janela aponta onde a janela está.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(s, c, next) { return next(s.startsWith('.') && !/\.[a-z]+$/i.test(s) ? s + '.js' : s, c) } })
const { rotuloDoResumo } = await import('../src/dominio/pedido.js')
const { motivoAntesDeCobrar } = await import('../src/dominio/resumoPedido.js')
assert.equal(rotuloDoResumo('aprovado baba', 44), 'Sinal pago')
assert.equal(rotuloDoResumo('aprovado baba', 0), 'Pago')
assert.equal(rotuloDoResumo('aguardando pagamento', 88), 'Aguardando pagamento')
assert.equal(motivoAntesDeCobrar({ pedido: { itens: [{ qtd: 1 }], janela: null }, clienteId: 'c', endereco: {} }, { fonteApi: true }),
  'Escolha a janela de entrega em Seleção do atendimento.')
console.log('4 cenários do sinal pago e da dica da janela aprovados.')
