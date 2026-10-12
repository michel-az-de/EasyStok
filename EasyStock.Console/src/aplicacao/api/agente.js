import * as acao from '../acoes'
import { sugerirResposta } from '../../infra/api/conversasApi'
import { sugestaoDaApi } from '../../infra/api/traducaoConversas'

// "Sugerir" do painel do agente no modo API (#1420): o EasyStok monta o contexto da conversa pelo id
// (histórico, transcrições, cadastro, caderno) e chama o mesmo agente do automático, só com consulta.
// Nada sai ao cliente: o texto chega ao painel, e "Usar no rascunho" o põe no campo de escrever.
// A marca "Precisa de você" e a ocorrência do modo simulado não nascem aqui: no modo API quem as
// grava é o EasyStok, e o navegador não finge o que o servidor não fez (F06).
export function criarAcoesAgenteApi({ despachar }) {
  let consultaAtual = 0
  let conversaAtual = null
  const pendentes = new Map()
  return {
    consultarAgente: (id) => {
      if (pendentes.has(id) && conversaAtual === id) return pendentes.get(id).resposta
      const consulta = ++consultaAtual
      conversaAtual = id
      despachar({ tipo: acao.AGENTE_PEDINDO, id })
      const registro = { chamada: pendentes.get(id)?.chamada ?? sugerirResposta(id) }
      registro.resposta = registro.chamada
        .then(dados => {
          if (consulta === consultaAtual) despachar({ tipo: acao.AGENTE_RESPONDEU, id, sugestao: sugestaoDaApi(dados) })
        })
        .catch(erro => {
          if (consulta === consultaAtual) despachar({ tipo: acao.AGENTE_FALHOU, id, erro: erro.message })
        })
        .finally(() => { if (pendentes.get(id) === registro) pendentes.delete(id) })
      pendentes.set(id, registro)
      return registro.resposta
    },
  }
}
