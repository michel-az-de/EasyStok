// Criadores de ação da biblioteca de respostas prontas (rodada 7, pedido do
// dono 24/09/2026). `AtendimentoProvider.jsx` chama `criarAcoesRespostas(despachar)`
// e espalha o resultado no objeto `acoes` do contexto, mesmo molde de `acoes/cardapio.js`.
import * as acao from '../acoes'

export function criarAcoesRespostas(despachar) {
  return {
    incluirRespostaPronta: (dados) => despachar({ tipo: acao.INCLUIR_RESPOSTA_PRONTA, dados }),
    editarRespostaPronta: (id, dados) => despachar({ tipo: acao.EDITAR_RESPOSTA_PRONTA, id, dados }),
    // Nunca apaga: marca arquivada, e a mesma ação restaura.
    alternarArquivamentoRespostaPronta: (id) => despachar({
      tipo: acao.ALTERNAR_ARQUIVAMENTO_RESPOSTA_PRONTA, id,
    }),
  }
}
