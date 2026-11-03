import { moeda } from './formato'

// #1884 ("Quanto fica?"): o que o servidor calculou para a comanda antes de gerar a cobrança (frete, total
// com frete, sinal da encomenda e restante). O console só mostra; a conta é do servidor, que usa as mesmas
// regras do pedido (zona do CEP, porção, sinal de 50% do total com o frete).

// A simulação só faz sentido com pedido ainda não criado, itens e janela escolhida (`janelaId|data`).
export const simulacaoVale = (pedido) =>
  Boolean(pedido && !pedido.pedidoId && pedido.itens?.length > 0 && String(pedido.janela ?? '').includes('|'))

// Linhas da comanda, na ordem em que se lê: frete, total com frete e, na encomenda, o sinal e o restante.
// Sem cotação não há número de frete, e nenhum total ou sinal é inventado.
export function linhasDaSimulacao(simulacao) {
  if (!simulacao) return null
  if (simulacao.freteACombinar) {
    return [{ chave: 'frete', texto: 'Frete a combinar com o cliente.' }]
  }
  const linhas = [
    { chave: 'frete', texto: `Frete ${moeda(simulacao.frete)}` },
    { chave: 'total', texto: `Total com frete ${moeda(simulacao.total)}` },
  ]
  if (simulacao.encomenda && simulacao.sinal != null) {
    linhas.push({
      chave: 'sinal',
      texto: `Encomenda para outro dia: sinal de ${moeda(simulacao.sinal)} antes da produção e ${moeda(simulacao.restante)} na entrega.`,
    })
  }
  return linhas
}
