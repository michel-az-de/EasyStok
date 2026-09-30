// Criadores de ação do Cardápio editável (rodada 5, US-020, RN-15).
// `AtendimentoProvider.jsx` chama `criarAcoesCardapio(despachar)` e espalha o
// resultado no objeto `acoes` do contexto, mesmo molde de `acoes/cliente.js`.
import * as acao from '../acoes'

export function criarAcoesCardapio(despachar) {
  return {
    incluirItemCardapio: (dados) => despachar({ tipo: acao.INCLUIR_ITEM_CARDAPIO, dados }),
    editarItemCardapio: (sku, dados) => despachar({ tipo: acao.EDITAR_ITEM_CARDAPIO, sku, dados }),
    // Nunca apaga: marca a hora que saiu, e a mesma ação repõe.
    alternarRemocaoItemCardapio: (sku, agora) => despachar({
      tipo: acao.ALTERNAR_REMOCAO_ITEM_CARDAPIO, sku, agora,
    }),
    confirmarValidacaoItem: (sku) => despachar({ tipo: acao.CONFIRMAR_VALIDACAO_ITEM, sku }),
  }
}
