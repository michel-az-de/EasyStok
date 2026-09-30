// Criadores de ação da frente Anexos (rodada 7, pedido do dono 24/09/2026
// 04h12). `AtendimentoProvider.jsx` chama `criarAcoesAnexos(despachar)` e
// espalha o resultado no objeto `acoes` do contexto, mesmo molde de
// `acoes/cardapio.js`. Sem gerador de id externo: `dominio/anexos.js` tira o
// id do nome, igual `dominio/cardapio.js` tira o sku (nenhuma feature
// importa infra para pedir `proximoId`).
import * as acao from '../acoes'

export function criarAcoesAnexos(despachar) {
  return {
    incluirPeca: (dados, agora) => despachar({ tipo: acao.INCLUIR_PECA, dados, agora }),
    editarPeca: (id, dados) => despachar({ tipo: acao.EDITAR_PECA, id, dados }),
    tirarPeca: (id) => despachar({ tipo: acao.TIRAR_PECA, id }),
  }
}
