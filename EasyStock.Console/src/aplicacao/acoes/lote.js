// Criadores de ação da frente Lote de papel (rodada 8, US-042, D6, UC-04 E1).
// Mesmo molde de aplicacao/acoes/<tema>.js: só mexe em AtendimentoProvider.jsx
// na linha de import e na linha que espalha `criarAcoesLote(despachar)`.
import * as acao from '../acoes'
import { proximoId } from '../../infra/repositorioConversas'

export function criarAcoesLote(despachar) {
  return {
    conexaoCaiu: (agora) => despachar({ tipo: acao.CONEXAO_CAIU, agora }),
    conexaoVoltou: (agora) => despachar({ tipo: acao.CONEXAO_VOLTOU, agora }),
    lancarLotePapel: (selecoes, agora) => despachar({
      tipo: acao.LANCAR_LOTE_PAPEL, selecoes, agora, loteId: proximoId('lote'),
    }),
  }
}
