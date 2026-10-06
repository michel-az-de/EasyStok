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

// Dinheiro que entrou no pedido.
export const totalPago = (pedido) =>
  pagamentosDoPedido(pedido).reduce((soma, p) => soma + p.valor, 0)

// Parte da comanda já quitada por cobrança paga.
const totalCoberto = (pedido) =>
  pagamentosDoPedido(pedido).reduce((soma, p) => soma + p.coberto, 0)

// Falta pagar do PEDIDO inteiro (comanda menos o que as cobranças pagas
// quitaram), diferente do `faltaPagar` de dominio/cobranca.js, que é só a
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
  const totalLiquido = totalDoPedido(pedido, cardapio) - (pedido.valorDesconto ?? 0)
  return Math.max(totalLiquido - totalCoberto(pedido), 0)
}

// Item novo depois do pagamento (defeito c, seção 3.2): só acende quando a
// cobrança ativa já foi paga e a comanda cresceu além do que foi quitado.
// Vale para o segundo, terceiro acréscimo: o que já caiu está no arquivo.
export function diferencaAposPagamento(pedido, cardapio) {
  if (!pedido?.cobranca?.pagaEm) return 0
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

// Pagamentos de um trecho de atendimento (seção 5, resumo): tudo que caiu
// a partir de `desde` (instante numérico). Sem `desde`, devolve tudo.
export function pagamentosDoTrecho(pedido, desde = null) {
  const pagamentos = pagamentosDoPedido(pedido)
  if (desde == null) return pagamentos
  return pagamentos.filter((p) => p.em >= desde)
}
