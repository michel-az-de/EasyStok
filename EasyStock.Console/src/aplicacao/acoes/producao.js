// Criadores de ação da frente Produção e cardápio (rodada 13, issue #43).
// Mesmo molde de `acoes/cardapio.js`: `AtendimentoProvider.jsx` chama
// `criarAcoesProducao(despachar)` e espalha o resultado no contexto.
import * as acao from '../acoes'
import { proximoId } from '../../infra/repositorioConversas'

export function criarAcoesProducao(despachar) {
  return {
    registrarProducao: (dados, agora) => despachar({
      tipo: acao.PRODUCAO_REGISTRAR_LOTE, ...dados, agora, loteId: proximoId('lote-producao'),
    }),
    ajustarContagemProducao: (dados, agora) => despachar({
      tipo: acao.PRODUCAO_AJUSTAR_CONTAGEM, ...dados, agora, ajusteId: proximoId('ajuste-producao'),
    }),
  }
}
