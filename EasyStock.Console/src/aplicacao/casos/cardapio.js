// Casos de reducer do Cardápio editável (rodada 5, US-020, RN-15). Ações
// previstas em `aplicacao/acoes.js`: INCLUIR_ITEM_CARDAPIO, EDITAR_ITEM_CARDAPIO,
// ALTERNAR_REMOCAO_ITEM_CARDAPIO, CONFIRMAR_VALIDACAO_ITEM. Mexe em
// `estado.catalogo`, nunca em `estado.conversas` (isso é de `casos/comanda.js`).
import * as acao from '../acoes'
import {
  alternarRemocaoItem, confirmarValidacaoItem, editarItemCardapio, incluirItemCardapio,
} from '../../dominio/cardapio'

export const casosCardapio = {
  // RN-15: o item nasce em validação, sem saldo lançado. `adicionaisSelecionados`
  // não é campo do item, é a linha do mapa `catalogo.adicionais` por sku.
  [acao.INCLUIR_ITEM_CARDAPIO]: (estado, { dados }) => {
    const { adicionaisSelecionados, ...camposItem } = dados
    const { cardapio, sku } = incluirItemCardapio(estado.catalogo.cardapio, camposItem)
    return {
      ...estado,
      catalogo: {
        ...estado.catalogo,
        cardapio,
        adicionais: { ...estado.catalogo.adicionais, [sku]: adicionaisSelecionados ?? [] },
      },
    }
  },

  [acao.EDITAR_ITEM_CARDAPIO]: (estado, { sku, dados }) => {
    const { adicionaisSelecionados, ...camposItem } = dados
    return {
      ...estado,
      catalogo: {
        ...estado.catalogo,
        cardapio: editarItemCardapio(estado.catalogo.cardapio, sku, camposItem),
        adicionais: { ...estado.catalogo.adicionais, [sku]: adicionaisSelecionados ?? [] },
      },
    }
  },

  // Tira ou repõe pela mesma ação (alterna). Nunca apaga a linha: histórico
  // continua achando o item pelo sku.
  [acao.ALTERNAR_REMOCAO_ITEM_CARDAPIO]: (estado, { sku, agora }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      cardapio: alternarRemocaoItem(estado.catalogo.cardapio, sku, new Date(agora).toISOString()),
    },
  }),

  [acao.CONFIRMAR_VALIDACAO_ITEM]: (estado, { sku }) => ({
    ...estado,
    catalogo: { ...estado.catalogo, cardapio: confirmarValidacaoItem(estado.catalogo.cardapio, sku) },
  }),
}
