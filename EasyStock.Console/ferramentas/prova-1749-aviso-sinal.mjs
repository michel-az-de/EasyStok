import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(s, c, proximo) {
  if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) return proximo(s + '.js', c)
  return proximo(s, c)
} })
const { avisoDeSinalAntesDeGerar } = await import('../src/dominio/resumoPedido.js')
const agora = Date.parse('2026-10-14T01:00:00Z') // Ainda é dia 13 na loja.
const pedido = { estado: 'aguardando', itens: [{ sku: 'item', qtd: 1 }], meio: 'maquininha', janela: 'janela|2026-10-14' }
const opcoes = { fonteApi: true, agora }
const copia = JSON.stringify(pedido)
assert.equal(avisoDeSinalAntesDeGerar(pedido, opcoes), 'Encomenda para outro dia: sinal de 50% antes da produção. Restante na entrega.')
assert.ok(avisoDeSinalAntesDeGerar({ ...pedido, meio: null }, opcoes), 'O picker avisa antes do clique que já gera')
for (const meio of ['pix', 'cartao-link', 'vale-refeicao']) {
  assert.equal(avisoDeSinalAntesDeGerar({ ...pedido, meio }, opcoes), null)
}
for (const mudanca of [
  { janela: 'janela|2026-10-13' }, { janela: 'janela|2026-10-12' }, { janela: 'demo' }, { janela: null },
  { janela: 'janela|2026-99-99' }, { pedidoId: 'existente' }, { cobranca: {} }, { estado: 'cancelado' }, { itens: [] },
]) assert.equal(avisoDeSinalAntesDeGerar({ ...pedido, ...mudanca }, opcoes), null)
assert.equal(avisoDeSinalAntesDeGerar(pedido, { ...opcoes, fonteApi: false }), null)
assert.equal(avisoDeSinalAntesDeGerar(null, opcoes), null)
assert.equal(JSON.stringify(pedido), copia, 'O aviso não altera regra ou estado do pedido')
console.log('ok aviso: maquininha/futuro e picker; hoje, demo, outros meios e pedido criado sem aviso; fuso da loja preservado')
