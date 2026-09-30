import { itensDetalhados, totalDoPedido } from './pedido'
import { horaCurta, moeda } from './formato'
import { faixaDepoisDeEntre } from './entrega'
import { nomeDoMeio } from './cobranca'

// Texto do pedido que vai para o cliente. Fica no domínio porque é regra de
// negócio, não formatação de tela: é o que a casa se compromete a entregar.
export function resumoParaCliente(pedido, cardapio, janela) {
  const linhas = itensDetalhados(pedido, cardapio)
    .map((l) => `• ${l.qtd}× ${l.produto?.nome} (${l.produto?.porcao})`
      + (l.obs ? ` — ${l.obs}` : ''))
  return [
    `Pedido ${pedido.numero}`,
    ...linhas,
    `Total: ${moeda(totalDoPedido(pedido, cardapio))}`,
    janela ? `Entrega entre ${faixaDepoisDeEntre(janela.faixa)}` : null,
  ].filter(Boolean).join('\n')
}

export const pedidoPodeSerGerado = (pedido) =>
  Boolean(pedido) && pedido.itens.length > 0 && pedido.estado === 'aguardando'

// Rodada 12 (issue #14, feedback da Thatiane no vídeo): "Enviar comanda" não
// reagia. O botão manda o resumo e a cobrança para o CLIENTE, na conversa; a
// cozinha recebe o pedido sozinha quando o pagamento entra (US-035). O rótulo
// passa a dizer o destino, e a tela nunca mais mostra o botão morto: ou ele
// envia, ou a linha diz por que não (enviada, já pago, vazia, bloqueado).
export const ROTULO_ENVIAR = 'Enviar ao cliente'

export function situacaoDoEnvio(pedido, { bloqueado = false } = {}) {
  if (pedido?.cobranca) {
    return {
      chave: 'enviada', pode: false,
      texto: `Enviada ao cliente às ${horaCurta(pedido.cobranca.criadaEm)}. Para mudar, use a Cobrança abaixo.`,
    }
  }
  if (pedido && pedido.estado !== 'aguardando') {
    return { chave: 'andou', pode: false, texto: 'Pedido já passou da cobrança. Não há o que enviar ao cliente.' }
  }
  if (!pedido || pedido.itens.length === 0) {
    return { chave: 'vazia', pode: false, texto: 'Comanda vazia. Abra o cardápio para montar o pedido.' }
  }
  if (bloqueado) {
    return { chave: 'bloqueado', pode: false, texto: 'Cliente bloqueado. Nada sai para ele.' }
  }
  return { chave: 'pode', pode: true, texto: null }
}

// Aviso que aparece na ficha logo depois do envio: o que saiu, para quem, e
// quando o pedido chega na cozinha. Mesma regra de `criarCobranca`: tem link,
// espera o pagamento; sem link (maquininha, vale), a esteira já andou.
export function avisoDoEnvio(cobranca, nomeCliente) {
  const nome = nomeDoMeio(cobranca?.meio)
  if (cobranca?.link) {
    return `Comanda enviada para ${nomeCliente} com o ${nome}. Entra na cozinha quando o pagamento cair.`
  }
  return `Comanda enviada para ${nomeCliente}. Pagamento na entrega (${nome}), o pedido já está na cozinha.`
}
