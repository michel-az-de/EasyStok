// Quanto já foi pago no pedido: UMA fonte de verdade (integração da onda
// seguinte, registro 38). Antes havia duas: `pedido.cobranca` (escrita por
// todo reducer de pagamento) e o livro-razão `pedido.pagamentos` (lido pelo
// resumo da F5 e pelo cartão de Entregas, mas que nenhum reducer escrevia).
//
// A fonte é a COBRANÇA. `pedido.cobranca` é a ativa; quando uma cobrança paga
// é substituída por outra (complemento de item novo ou diferença de valor,
// REENVIAR_COBRANCA), ela vai para `pedido.pagamentos`, que passa a ser só o
// arquivo das cobranças pagas que saíram de cena. Ninguém soma à mão: tudo
// lê `pagamentosDoPedido`.
//
// Cada registro: { valor (dinheiro que entrou), coberto (valor da cobrança
// que ele quitou), em, meio, cobrancaId }. `valor` e `coberto` só diferem
// quando o cliente pagou diferente do cobrado (divergência).

import { totalDoPedido } from './pedido'

export const registroDaCobranca = (cobranca) => ({
  valor: cobranca.valorPago ?? cobranca.valor,
  coberto: cobranca.valor,
  em: cobranca.pagaEm,
  meio: cobranca.meio ?? 'pix',
  cobrancaId: cobranca.id,
})

// Arquivadas + a ativa, se paga.
//
// Estornada continua entrando aqui DE PROPÓSITO: `dominio/resumoAtendimento.js`
// (`receitaDoTrecho`) lê o bruto por este caminho e subtrai o estorno como
// linha própria (`bruto - estorno = líquido`, decisão 36). Tirar a estornada
// daqui faria o líquido subtrair duas vezes o mesmo valor.
export const pagamentosDoPedido = (pedido) => pedido?.pagamentosApi ?? [
  ...(pedido?.pagamentos ?? []),
  ...(pedido?.cobranca?.pagaEm ? [registroDaCobranca(pedido.cobranca)] : []),
]

// O pagamento que "Desfazer o último" mostra e manda: a tela e a chamada leem daqui.
export const ultimoPagamentoApi = (pedido) => [...(pedido?.pagamentosApi ?? [])].sort((a, b) => b.em - a.em)[0] ?? null

// Dinheiro que entrou no pedido.
export const totalPago = (pedido) =>
  pagamentosDoPedido(pedido).reduce((soma, p) => soma + p.valor, 0)

// Parte da comanda já quitada por cobrança paga.
const totalCoberto = (pedido) =>
  pedido?.alocadoApi ?? pagamentosDoPedido(pedido).reduce((soma, p) => soma + p.coberto, 0)

// Falta pagar do PEDIDO inteiro (comanda menos o que as cobranças pagas
// quitaram), diferente do `faltaDaCobranca` de dominio/cobranca.js, que é só a
// divergência de uma cobrança.
//
// `pedido.valorDesconto` (frente Fidelidade e cupons, rodada 13, issue #45,
// registro 107) desconta aqui direto, sem importar `dominio/fidelidade.js`
// (evita módulo financeiro de base depender de módulo de frente): sem isto,
// um pedido com cupom pago integralmente aparecia com "item novo depois do
// pagamento" pelo valor do PRÓPRIO desconto, oferecendo "Cobrar diferença" de
// um valor que o cliente não deve (achado ao validar na tela, registro 107).
export function faltaPagar(pedido, cardapio) {
  if (!pedido) return 0
  const totalLiquido = (pedido.totalApi ?? totalDoPedido(pedido, cardapio)) - (pedido.valorDesconto ?? 0)
  return Math.max(totalLiquido - totalCoberto(pedido), 0)
}

// #2005 (console-19): o saldo do pedido criado no EasyStok, numa conta só: total menos o alocado (sem o
// alocado, o recebido), em centavos exatos e nunca negativo. A Barra, a Cobrança e o Extrato liam
// `totalApi - alocado` cada um por si, e só o Extrato arredondava.
export const saldoApi = (pedido) =>
  Math.max(Math.round(((pedido?.totalApi ?? 0) - (pedido?.alocadoApi ?? pedido?.totalPagoApi ?? 0)) * 100) / 100, 0)

// #2005 (console-01): encomenda com o sinal pago e o resto combinado para a entrega. O que falta pagar é o
// restante da entrega, não "item novo depois do pagamento": já estava no total quando o sinal foi cobrado.
export function restanteDoSinal(pedido) {
  if (!pedido?.sinalNaReserva || !pedido.cobranca?.pagaEm || pedido.estado === 'cancelado') return 0
  return saldoApi(pedido)
}

// Item novo depois do pagamento (defeito c, seção 3.2): só acende quando a
// cobrança ativa já foi paga e a comanda cresceu além do que foi quitado.
// Vale para o segundo, terceiro acréscimo: o que já caiu está no arquivo.
export function diferencaAposPagamento(pedido, cardapio) {
  if (!pedido?.cobranca?.pagaEm || pedido.estado === 'cancelado') return 0
  if (restanteDoSinal(pedido) > 0) return 0
  return faltaPagar(pedido, cardapio)
}

// Complemento a receber (cartão de Entregas): diferença ainda sem cobrança,
// ou cobrança de diferença emitida e não paga.
export function complementoAReceber(pedido, cardapio) {
  const cobranca = pedido?.cobranca
  if (!cobranca) return 0
  if (cobranca.diferenca && !cobranca.pagaEm) return cobranca.valor
  return diferencaAposPagamento(pedido, cardapio)
}

// #1776 (item 19): no cartão do KDS (Entregas, Cozinha) "pago" é o saldo do servidor zerado
// (`aReceber`, de `SaldoFinanceiroPedido`), não "tem pagamento": o sinal de R$ 25 num pedido de
// R$ 50 deixa R$ 25 para a porta. API antiga, sem o campo, cai no "tem pagamento" de antes.
export const aReceberDoCartao = (p) => (typeof p?.aReceber === 'number' ? Math.max(p.aReceber, 0) : null)
export const quitadoNoCartao = (p) => {
  const falta = aReceberDoCartao(p)
  return falta === null ? Boolean(p?.pagoEm) : falta === 0
}

// Marcar entregue com saldo a receber: a API responde 409 com este código até a confirmação.
export const CODIGO_SALDO_A_RECEBER = 'SALDO_A_RECEBER'
export const pedeConfirmacaoDoSaldo = (erro) => erro?.codigo === CODIGO_SALDO_A_RECEBER

// #1839: extrato do pedido na modal Pagamento, com o que o EasyStok já devolve (itens, frete,
// pagamentos e saldo): de onde veio cada real, sem abrir o Financeiro. Nulo fora do modo API.
// J2 (#1990): `aDevolver` vem do EasyStok (pedido cancelado com recebimento); o extrato diz quanto
// falta devolver em vez de "nada a cobrar".
export function extratoDoPedido(pedido, { aDevolver = 0 } = {}) {
  if (!pedido?.pedidoId || typeof pedido.totalApi !== 'number') return null
  const frete = pedido.frete ?? 0
  const pagamentos = [...(pedido.pagamentosApi ?? [])].sort((a, b) => (a.em ?? 0) - (b.em ?? 0))
  const recebido = pedido.totalPagoApi ?? pagamentos.reduce((soma, p) => soma + p.valor, 0)
  // O alocado do servidor reúne recebimentos, devoluções e créditos; o delta não identifica a origem.
  // Cancelar encerra a cobrança, mas preserva o histórico e qualquer saldo que ainda precisa de acerto.
  const pago = pedido.alocadoApi ?? recebido
  const cancelado = pedido.estado === 'cancelado'
  return {
    cancelado,
    itens: Math.max(pedido.totalApi - frete, 0),
    frete,
    total: pedido.totalApi,
    pagamentos: pagamentos.map((p) => ({ id: p.pagamentoId, em: p.em, meio: p.meio, valor: p.valor })),
    ajuste: Math.round((pago - recebido) * 100) / 100,
    pago,
    saldo: cancelado ? 0 : saldoApi({ totalApi: pedido.totalApi, alocadoApi: pago }),
    aDevolver: cancelado && aDevolver > 0 ? aDevolver : 0,
  }
}
