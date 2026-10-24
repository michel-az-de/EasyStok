import {
  ajustarSaldo, alternarDisponibilidade, baixarSaldo, devolverSaldo, itemPorSku, situacaoDeEstoque,
  textoDoAlerta,
} from '../../dominio/cardapio'
import { comItem, comObservacao, novoPedido, pedidoEncerrado, semItem } from '../../dominio/pedido'
import * as acao from '../acoes'
import { comBaixaDeProducao, comDevolucaoDeProducao, comMensagem, mapear } from '../reducer/auxiliares'

// A comanda que a dona monta antes de o pedido existir no EasyStok (rascunho local) e o saldo do dia. O modo API
// também despacha estes casos: `aplicacao/api/comanda.js` e `api/cardapio.js` usam as ações locais correspondentes.

export const casosRascunhoDaComanda = {
  // #1686 (item 3): pedido entregue ou cancelado não recebe item (a trava do ADICIONAR_ITEM fica),
  // mas a mesma conversa abre uma comanda nova, em rascunho. O pedido antigo segue no EasyStok e
  // a leitura dele não apaga o rascunho (`pedidoMesclado`). Pedido em andamento não é trocado.
  [acao.NOVA_COMANDA]: (estado, { id, numeroPedido }) => mapear(estado, id, (c) => (
    c.pedido && !pedidoEncerrado(c.pedido) ? c : { ...c, pedido: novoPedido(numeroPedido) }
  )),

  // Saldo zero avisa, nunca trava a venda.
  [acao.ADICIONAR_ITEM]: (estado, {
    id, item, numeroPedido, alertaId, agora,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    // Pedido cancelado não edita comanda (dominio/pedido.js, pedidoEncerrado):
    // sem esta trava, um item novo entrava numa comanda que já morreu.
    if (pedidoEncerrado(conversa?.pedido)) return estado
    const situacao = situacaoDeEstoque(item)
    const comProduto = mapear(estado, id, (c) => ({
      ...c,
      pedido: comItem(c.pedido ?? novoPedido(numeroPedido), item.sku),
    }))
    return {
      ...comProduto,
      catalogo: { ...estado.catalogo, cardapio: baixarSaldo(estado.catalogo.cardapio, item.sku) },
      // Rodada 13 (issue #43, RN-50): sku com lote lançado baixa dele; sem
      // lote, `producao` não muda (comBaixaDeProducao é no-op).
      producao: comBaixaDeProducao(estado.producao, item.sku, 1, agora),
      alertasEstoque: situacao.alerta
        ? [...estado.alertasEstoque, { id: alertaId, texto: textoDoAlerta(item) }]
        : estado.alertasEstoque,
    }
  },

  [acao.REMOVER_ITEM]: (estado, { id, sku }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const linha = conversa?.pedido?.itens.find((l) => l.sku === sku)
    if (!linha) return estado
    return {
      ...mapear(estado, id, (c) => ({ ...c, pedido: semItem(c.pedido, sku) })),
      catalogo: { ...estado.catalogo, cardapio: devolverSaldo(estado.catalogo.cardapio, sku, linha.qtd) },
      producao: comDevolucaoDeProducao(estado.producao, sku, linha.qtd),
    }
  },

  // O +/- da comanda soma ou tira saldo igual ao caminho de ADICIONAR_ITEM,
  // então a mesma checagem de saldo zero roda aqui: aumentar quantidade pelo
  // +/- também pode zerar o item, e antes disso não nascia alerta nenhum
  // (QA2-18).
  [acao.AJUSTAR_QUANTIDADE]: (estado, {
    id, sku, delta, alertaId, agora,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const linha = conversa?.pedido?.itens.find((l) => l.sku === sku)
    if (!linha) return estado
    const nova = linha.qtd + delta
    if (nova <= 0) return casosRascunhoDaComanda[acao.REMOVER_ITEM](estado, { id, sku })
    const item = itemPorSku(estado.catalogo.cardapio, sku)
    const situacao = delta > 0 && item ? situacaoDeEstoque(item) : null
    return {
      ...mapear(estado, id, (c) => ({
        ...c,
        pedido: {
          ...c.pedido,
          itens: c.pedido.itens.map((l) => (l.sku === sku ? { ...l, qtd: nova } : l)),
        },
      })),
      catalogo: {
        ...estado.catalogo,
        cardapio: delta > 0
          ? baixarSaldo(estado.catalogo.cardapio, sku)
          : devolverSaldo(estado.catalogo.cardapio, sku),
      },
      // Rodada 13 (issue #43): mesmo caminho de ADICIONAR_ITEM/REMOVER_ITEM.
      producao: delta > 0
        ? comBaixaDeProducao(estado.producao, sku, Math.abs(delta), agora)
        : comDevolucaoDeProducao(estado.producao, sku, Math.abs(delta)),
      alertasEstoque: situacao?.alerta
        ? [...estado.alertasEstoque, { id: alertaId, texto: textoDoAlerta(item) }]
        : estado.alertasEstoque,
    }
  },

  // Texto livre por item, sem checagem de saldo nem toque em estoque: é
  // anotação da comanda, não mudança de quantidade (QA2-01).
  [acao.AJUSTAR_OBSERVACAO]: (estado, { id, sku, texto }) =>
    mapear(estado, id, (c) => (c.pedido ? { ...c, pedido: comObservacao(c.pedido, sku, texto) } : c)),

  // Ligar e desligar item do dia é a cozinha dizendo o que existe, não venda.
  // O item sai do DIA, nunca da lista (RN-16): quem procura continua achando,
  // com o estado escrito.
  [acao.ALTERNAR_DISPONIBILIDADE]: (estado, { sku }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      cardapio: alternarDisponibilidade(estado.catalogo.cardapio, sku),
    },
  }),

  // Acerto de saldo na mão. É ela contando o congelador, e o número dela vence o
  // do sistema. Era a frustração do áudio 14: produzir um quilo, vender 500 g e
  // o sistema dizer que não tem.
  [acao.AJUSTAR_SALDO]: (estado, { sku, delta }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      cardapio: ajustarSaldo(estado.catalogo.cardapio, sku, delta),
    },
  }),

  // Escolher janela ocupa vaga e trocar devolve, sem ninguém somar nada: a
  // ocupação é contada dos pedidos (dominio/entrega.js), então mover o pedido JÁ
  // é mover a vaga. Trocar de janela nunca exige cancelar e refazer o pedido,
  // erro que o Zé Delivery comete e que a Baymard mede em mercado online.
  //
  // Encaixe forçado de antes é apagado aqui: se ela voltou para uma janela com
  // vaga, o estouro daquele pedido deixou de existir.
  [acao.ESCOLHER_JANELA]: (estado, { id, janela }) =>
    mapear(estado, id, (c) => (c.pedido
      ? { ...c, pedido: { ...c.pedido, janela, encaixeForcado: null } }
      : c)),

  // Encaixe fora de vaga. No automático isso não existe: o agente passa para a
  // dona (D7 e RN-53). Aqui ela força, com confirmação, e fica registrado o que
  // estourou, quando e por quê. Registro na conversa também, porque quem vê a
  // conversa depois precisa entender por que aquela janela tem gente demais.
  [acao.FORCAR_ENCAIXE]: (estado, { id, janela, faixa, motivo, agora, mensagemId }) =>
    mapear(estado, id, (c) => (c.pedido
      ? comMensagem(
        {
          ...c,
          pedido: {
            ...c.pedido,
            janela,
            encaixeForcado: { em: agora, faixa, motivo },
          },
        },
        {
          dir: 'sistema',
          texto: `Encaixe forçado na janela de ${faixa}. ${motivo}`,
        },
        { id: mensagemId, agora },
      )
      : c)),
}
