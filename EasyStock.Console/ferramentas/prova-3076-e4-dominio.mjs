// #3076 (E4): as regras do detalhe e da lista que não precisam de navegador. Prova o texto do pagamento no cabeçalho
// (cupom pago mostra "Pago"; "Quitado" só no quitado do histórico, sem pagamento; cancelado com saldo zero, nunca o
// total), o botão por situação, o "quando entregar" e o filtro "Entrega: Hoje" no fuso da loja.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const dominio = await import('../src/dominio/pedidosCadastro.js')
const { consultaDePedidos, MOTIVOS_SEM_BAIXA_NO_DETALHE } = dominio
// O formato de moeda usa espaço inseparável; a prova compara com espaço comum.
const comum = (texto) => texto.replace(/\s/g, ' ')
const pagamentoNoDetalhe = (pedido) => { const r = dominio.pagamentoNoDetalhe(pedido); return r && { ...r, texto: comum(r.texto) } }
const quandoEntregar = (pedido, agoraMs) => comum(dominio.quandoEntregar(pedido, agoraMs))
let testes = 0

// 1. Saldo da API: falta R$ 80,00, botão ligado com o saldo.
{
  const r = pagamentoNoDetalhe({ status: 'pronto', total: 132.3, totalPago: 52.3, aReceber: 80 })
  assert.equal(r.texto, 'Falta receber R$ 80,00'); assert.deepEqual(r.botao, { ligado: true, valor: 80 }); testes++
}
// 2. Cupom: total R$ 100,00, cupom R$ 20,00, R$ 80,00 pagos. A API devolve aReceber 0; o cabeçalho diz "Pago", sem botão.
{
  const r = pagamentoNoDetalhe({ status: 'entregue', total: 100, totalPago: 80, aReceber: 0 })
  assert.equal(r.texto, 'Pago', 'cupom pago não é "Quitado"'); assert.equal(r.botao, null); assert.equal(r.falta, 0); testes++
}
// 3. Quitado pelo import do histórico: nada pago, saldo zero.
{
  assert.equal(pagamentoNoDetalhe({ status: 'entregue', total: 100, totalPago: 0, aReceber: 0 }).texto, 'Quitado'); testes++
}
// 4. Pago a mais.
{
  assert.equal(pagamentoNoDetalhe({ status: 'entregue', total: 100, totalPago: 110, aReceber: 0 }).texto, 'Recebido R$ 10,00 a mais'); testes++
}
// 5. Cancelado: saldo zero e sem botão, mesmo com aReceber da API ou total aberto; nunca "Falta receber R$ 132,30".
{
  const r = pagamentoNoDetalhe({ status: 'cancelado', total: 132.3, totalPago: 0, aReceber: 132.3 })
  assert.equal(r.falta, 0); assert.equal(r.botao, null); assert.doesNotMatch(r.texto, /132,30/)
  const comRecebido = pagamentoNoDetalhe({ status: 'cancelado', total: 132.3, totalPago: 50 })
  assert.equal(comRecebido.botao, null); assert.match(comRecebido.texto, /50,00/, 'cancelado mostra o que foi recebido'); testes++
}
// 6. Pré-operacional com saldo: botão desligado com o motivo da tabela da spec.
{
  for (const status of ['rascunho', 'aguardando_aprovacao_baba', 'aguardando_pagamento']) {
    const r = pagamentoNoDetalhe({ status, total: 80, totalPago: 0, aReceber: 80 })
    assert.deepEqual(r.botao, { ligado: false, motivo: MOTIVOS_SEM_BAIXA_NO_DETALHE[status] }, status)
  }
  testes++
}
// 7. Sem aReceber (API antiga): total menos pago.
{
  assert.equal(pagamentoNoDetalhe({ status: 'pronto', total: 100, totalPago: 30 }).falta, 70); testes++
}
// 8. Quando entregar, no fuso de Brasília. Agora: 22/11/2026 10:00 em Brasília (13:00Z).
const agora = Date.parse('2026-11-22T13:00:00Z')
{
  assert.equal(quandoEntregar({ agendadoParaEm: '2026-11-22T17:30:00Z' }, agora), 'Hoje, 14:30')
  assert.equal(quandoEntregar({ agendadoParaEm: '2026-11-23T02:30:00Z' }, agora), 'Hoje, 23:30', '23:30 de Brasília ainda é hoje')
  assert.equal(quandoEntregar({ agendadoParaEm: '2026-11-23T12:00:00Z' }, agora), 'Amanhã, 09:00')
  assert.equal(quandoEntregar({ agendadoParaEm: '2026-11-21T12:00:00Z' }, agora), 'Ontem, 09:00')
  assert.match(quandoEntregar({ agendadoParaEm: '2026-11-30T12:00:00Z' }, agora), /^30\/11, 09:00$/)
  assert.equal(quandoEntregar({}, agora), 'Na hora'); testes++
}
// 9. Filtro "Entrega: Hoje": o dia de Brasília nos dois limites; "Qualquer dia" não manda nada; ordem de entrega asc.
{
  const hoje = consultaDePedidos({ entrega: 'hoje' }, Date.parse('2026-11-23T02:30:00Z'))
  assert.equal(hoje.entregaDesde, '2026-11-22'); assert.equal(hoje.entregaAte, '2026-11-22')
  const tudo = consultaDePedidos({}, agora)
  assert.equal('entregaDesde' in tudo, false); assert.equal('entregaAte' in tudo, false)
  const ordem = consultaDePedidos({ ordem: 'entrega' }, agora)
  assert.equal(ordem.sort, 'entrega'); assert.equal(ordem.order, 'asc'); testes++
}
console.log(`prova-3076-e4-dominio: ${testes} cenários ok`)
