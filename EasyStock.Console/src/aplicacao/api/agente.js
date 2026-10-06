import * as acao from '../acoes'
import { sugerirResposta } from '../../infra/api/conversasApi'
import { sugestaoDaApi } from '../../infra/api/traducaoConversas'

// "Sugerir" do painel do agente no modo API (#1420): o EasyStok monta o contexto da conversa pelo id
// (histórico, transcrições, cadastro, caderno) e chama o mesmo agente do automático, só com consulta.
// Nada sai ao cliente: o texto chega ao painel, e "Usar no rascunho" o põe no campo de escrever.
// A marca "Precisa de você" e a ocorrência do modo simulado não nascem aqui: no modo API quem as
// grava é o EasyStok, e o navegador não finge o que o servidor não fez (F06).
export function criarAcoesAgenteApi({ despachar }) {
  return {
    consultarAgente: async (id) => {
      despachar({ tipo: acao.AGENTE_PEDINDO, id })
      try {
        despachar({ tipo: acao.AGENTE_RESPONDEU, id, sugestao: sugestaoDaApi(await sugerirResposta(id)) })
      } catch (erro) {
        despachar({ tipo: acao.AGENTE_FALHOU, id, erro: erro.message })
      }
    },
  }
}
