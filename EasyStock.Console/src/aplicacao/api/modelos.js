import { listarModelos, enviarModelo, tentativaModelo, limparTentativaModelo } from '../../infra/api/modelosApi'
import { mensagemDaApi } from '../../infra/api/traducaoConversas'
import * as acao from '../acoes'

export function criarAcoesModelosApi({ despachar }) {
  const pendentes = new Map()
  return {
    listarModelos,
    prepararOutroModelo: limparTentativaModelo,
    enviarModelo: async (id, dados) => {
      const corpo = { nome: dados.nome, idioma: dados.idioma, parametros: dados.parametros.map(p => p.trim()) }
      const tentativaId = tentativaModelo(id, corpo)
      if (pendentes.has(tentativaId)) return pendentes.get(tentativaId)
      const envio = enviarModelo(id, { ...corpo, tentativaId }).then(m => {
        despachar({ tipo: acao.RECEBER_MODELO_API, id, mensagem: mensagemDaApi(m, id) })
        if (['Enviada', 'Entregue', 'Lida'].includes(m.status)) limparTentativaModelo(id)
        return m
      }).finally(() => pendentes.delete(tentativaId))
      pendentes.set(tentativaId, envio)
      return envio
    },
  }
}
