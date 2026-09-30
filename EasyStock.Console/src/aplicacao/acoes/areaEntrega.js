// Criadores de ação da tarefa Área de entrega (rodada 5, RN-09/RN-10/RN-11,
// UC-02). Arquivo próprio, no mesmo molde das frentes de
// `aplicacao/acoes/<tema>.js`: nenhuma outra frente mexe aqui, e este arquivo
// nunca mexe em `AtendimentoProvider.jsx` além da linha de import e a linha
// que espalha `criarAcoesAreaEntrega(despachar)`. `despachar` já é o
// `dispatch` do reducer.
import * as acao from '../acoes'
import { proximoId } from '../../infra/repositorioConversas'

export function criarAcoesAreaEntrega(despachar) {
  return {
    // UC-02: liberar, virar encomenda agendada ou recusar com cortesia. A
    // decisão em si (o que muda no cadastro) mora em `casos/areaEntrega.js`.
    decidirAreaEntrega: (id, decisao, agora) => despachar({
      tipo: acao.DECIDIR_AREA_ENTREGA, id, decisao, agora, notaId: proximoId('nota'),
    }),
  }
}
