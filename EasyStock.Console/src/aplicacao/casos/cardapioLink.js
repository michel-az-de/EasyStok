// Casos de reducer da Frente Cardápio por link (rodada 7, US-021). Arquivo
// desta frente: nenhuma outra frente edita este arquivo, e esta frente nunca
// edita `reducer.js` (ele já importa e espalha `casosCardapioLink` no objeto
// composto, igual às outras).
//
// Só um caso novo: o pagamento em si reaproveita CONFIRMAR_PAGAMENTO (já
// existe em reducer.js), despachado pela própria janela do site depois deste.
import * as acao from '../acoes'
import { itensDetalhados, pedidoEncerrado } from '../../dominio/pedido'
import { baixarSaldo } from '../../dominio/cardapio'
import { criarCobranca } from '../../dominio/cobranca'
import {
  MOTIVO_CONFERIR_PEDIDO_LINK, pedidoDoCarrinho, pedidoPrecisaConferir, textoCartaoPedidoLink,
} from '../../dominio/cardapioLink'
// Rodada 10 (registro 79, frente Simulação): o eco em "in" do pedido fechado
// no cardápio por link mora no domínio novo do cliente simulado, ao lado,
// não aqui. Este arquivo continua da Frente Cardápio por link; o único
// acréscimo é chamar o texto pronto.
import { textoEcoPedidoCardapioLink } from '../../dominio/clienteSimulado'

function mapear(estado, id, transformar) {
  return { ...estado, conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)) }
}

function comMensagem(conversa, mensagem, { id, agora }) {
  const paraOCliente = mensagem.dir !== 'sistema'
  return {
    ...conversa,
    atrasada: paraOCliente ? false : conversa.atrasada,
    ultimaEm: paraOCliente ? new Date(agora).toISOString() : conversa.ultimaEm,
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

export const casosCardapioLink = {
  // RN-23: o pedido nasce ANTES do pagamento. `itens` (com a observação de
  // cada linha, RN-20), `janela`, `endereco`, `meio`, `numero` e `emissao`
  // chegam prontos da janela do site (ela chamou infra/provedoresDeCobranca.js
  // do lado dela, o mesmo caminho do GERAR_PEDIDO): o reducer só grava.
  // Rodada 12 (#19): a conversa só sobe para "Precisa de você" quando o
  // cliente escreveu observação; sem ela, o pedido segue para a cobrança.
  [acao.CRIAR_PEDIDO_CARDAPIO_LINK]: (estado, {
    id, agora, mensagemId, numero, itens, janela, endereco, meio, emissao,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa || pedidoEncerrado(conversa.pedido) || itens.length === 0) return estado
    const pedido = pedidoDoCarrinho(numero, { itens }, janela, meio)
    const cobranca = criarCobranca(pedido, estado.catalogo.cardapio, agora, emissao)
    const texto = textoCartaoPedidoLink(pedido, estado.catalogo.cardapio, meio)
    // Rodada 10 (registro 79, "falta construir" 1, UC-01 passos 7 e 8): além
    // do cartão de sistema, uma mensagem "in", como se o próprio cliente
    // tivesse escrito assim que fechou o carrinho no link.
    const textoEco = textoEcoPedidoCardapioLink(itensDetalhados(pedido, estado.catalogo.cardapio))
    const cardapioComBaixa = itens.reduce(
      (cat, linha) => baixarSaldo(cat, linha.sku, linha.qtd), estado.catalogo.cardapio,
    )
    const em = new Date(agora).toISOString()
    return {
      ...mapear(estado, id, (c) => comMensagem(
        comMensagem({
          ...c,
          pedido: { ...pedido, cobranca },
          cliente: endereco ? { ...c.cliente, endereco } : c.cliente,
          passagem: !pedidoPrecisaConferir(itens) || (c.passagem && !c.passagem.assumida)
            ? c.passagem
            : { motivo: MOTIVO_CONFERIR_PEDIDO_LINK, em, assumida: false },
        }, { dir: 'in', texto: textoEco }, { id: `${mensagemId}-eco`, agora }),
        { dir: 'sistema', texto },
        { id: mensagemId, agora },
      )),
      catalogo: { ...estado.catalogo, cardapio: cardapioComBaixa },
    }
  },
}
