import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) return next(s + '.js', c); return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API=true; export const API_BASE=''" } : next(url, c) },
})
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { pagamentoDoRetrato } = await import('../src/dominio/avisoSonoro.js')
const { avisosDoRetrato, TIPOS_DE_AVISO } = await import('../src/dominio/aviso.js')
const dto = { pedidoId: 'pedido-controlado', status: 'aguardando', total: 37, totalPago: 0, alocado: 0, frete: 0, itens: [],
  cobranca: { cobrancaId: 'cobranca', provedor: 'na_entrega', status: 'Pendente', valor: 37, tentativa: 1 } }
const pedido = extra => pedidoDaApi({ ...dto, ...extra })
const retrato = p => new Map([['conversa', { nome: 'Cliente de teste', etapa: p?.estado ?? null, ...pagamentoDoRetrato(p) }]])
const pagamentos = (a, b) => avisosDoRetrato(retrato(a), retrato(b)).filter(a => a.tipo === TIPOS_DE_AVISO.PAGAMENTO_CONFIRMADO)
assert.equal(pagamentoDoRetrato(pedido()).pago, false, 'Na entrega sem receber não é pagamento confirmado')
assert.equal(pagamentos(null, pedido()).length, 0)
assert.equal(pagamentos(pedido(), pedido({ totalPago: 10, alocado: 10 })).length, 0, 'Parcial sem sinal não diz pagou o pedido')
assert.equal(pagamentos(pedido(), pedido({ totalPago: 0, alocado: 37 })).length, 0, 'Cupom/crédito sem recebimento não diz pagou')
assert.equal(pagamentos(pedido({ totalPago: 10, alocado: 10 }), pedido({ totalPago: 10, alocado: 37 })).length, 0, 'Cobertura sem novo recebimento não dispara pagamento')
const sinal = pedido({ sinalNaReserva: true, totalPago: 20, alocado: 20 })
assert.match(pagamentos(pedido(), sinal)[0]?.corpo ?? '', /pagou o sinal$/)
const quitado = pedido({ status: 'pronto', totalPago: 37, alocado: 37 })
assert.match(pagamentos(sinal, quitado)[0]?.corpo ?? '', /pagou o pedido$/)
assert.equal(pagamentos(quitado, quitado).length, 0, 'Polling/replay não repete aviso')
assert.equal(pagamentos(quitado, pedido({ status: 'entregue', totalPago: 37, alocado: 37 })).length, 0, 'Mudança operacional sem recebimento não repete aviso')
assert.equal(pagamentoDoRetrato({ estado: 'pago' }).pago, false, 'Sem evidência não afirma recebimento')
console.log('Pagamento: na entrega0, parcial, cupom, sinal, recebimento emPronto e replay conferidos com mapper real.')
